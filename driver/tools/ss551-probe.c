/* ss551-probe: command-line tool for exploring the SS-551 protocol. */
#define _POSIX_C_SOURCE 200809L
#include "ss551.h"

#include <hidapi.h>
#include <signal.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

static volatile sig_atomic_t g_stop;

static void on_sigint(int sig)
{
    (void)sig;
    g_stop = 1;
}

static void usage(void)
{
    fprintf(stderr,
            "usage: ss551-probe <command> [args]\n"
            "  info                     print serial number\n"
            "  listen                   print every input report (Ctrl+C to stop)\n"
            "  rawlisten                dump raw reports from all interfaces\n"
            "  brightness <0-100>       set screen brightness\n"
            "  wake | off               wake screen / turn screen off\n"
            "  clear [key]              clear one key image (default: all)\n"
            "  image <key> <file.jpg>   upload a JPEG to a key and refresh\n"
            "  raw <off>=<hex|str> ...  send CRT\\0\\0 packet, e.g. raw 5=STP or 5=MOD 10=01\n");
}

static void dump(const uint8_t *b, int n)
{
    /* Trailing zeros are padding; show only up to the last non-zero byte. */
    int last = n - 1;
    while (last > 0 && b[last] == 0)
        last--;
    for (int i = 0; i <= last; i++)
        printf("%02x%s", b[i], (i % 16 == 15) ? "\n      " : " ");
    printf(" | ");
    for (int i = 0; i <= last && i < 32; i++)
        putchar(b[i] >= 32 && b[i] < 127 ? b[i] : '.');
    putchar('\n');
}

static int cmd_listen(ss551_device *dev)
{
    signal(SIGINT, on_sigint);
    if (ss551_start_session(dev) < 0) {
        fprintf(stderr, "%s\n", ss551_error(dev));
        return 1;
    }
    printf("listening, press keys / turn knobs...\n");
    int idle = 0;
    while (!g_stop) {
        ss551_event ev;
        int r = ss551_read_event(dev, &ev, 500);
        if (r < 0) {
            fprintf(stderr, "%s\n", ss551_error(dev));
            return 1;
        }
        if (r == 0) {
            /* Keep the device in host mode, like the vendor app does. */
            if (++idle % 20 == 0)
                ss551_heartbeat(dev);
            continue;
        }
        switch (ev.type) {
        case SS551_EVENT_KEY:
            printf("KEY  key=0x%02x (%3d) state=0x%02x  ", ev.key, ev.key, ev.state);
            break;
        case SS551_EVENT_ACK:
            printf("ACK  ");
            break;
        case SS551_EVENT_MODE:
            printf("MODE mode=%d  ", ev.state);
            break;
        default:
            printf("RAW  ");
            break;
        }
        dump(ev.raw, ev.raw_len);
        fflush(stdout);
    }
    return 0;
}

/* Diagnostics: read every HID interface of the device directly via hidapi. */
static int cmd_rawlisten(void)
{
    hid_device *h[4];
    int iface[4], n = 0;
    struct hid_device_info *list = hid_enumerate(SS551_VID, SS551_PID);
    for (struct hid_device_info *it = list; it && n < 4; it = it->next) {
        h[n] = hid_open_path(it->path);
        if (!h[n]) {
            fprintf(stderr, "cannot open %s\n", it->path);
            continue;
        }
        hid_set_nonblocking(h[n], 1);
        iface[n] = it->interface_number;
        printf("opened %s (iface %d, usage page 0x%04x)\n", it->path, iface[n], it->usage_page);
        n++;
    }
    hid_free_enumeration(list);

    for (int i = 0; i < n; i++) {
        uint8_t buf[SS551_IN_PACKET + 1] = {0};
        int r = hid_get_input_report(h[i], buf, sizeof buf);
        printf("iface %d get_input_report -> %d  ", iface[i], r);
        if (r > 0)
            dump(buf, r);
        else
            printf("(%ls)\n", hid_error(h[i]));
    }

    signal(SIGINT, on_sigint);
    printf("listening on all interfaces, press keys / turn knobs...\n");
    while (!g_stop) {
        for (int i = 0; i < n; i++) {
            uint8_t buf[SS551_IN_PACKET];
            int r = hid_read(h[i], buf, sizeof buf);
            if (r > 0) {
                printf("iface %d [%3d B] ", iface[i], r);
                dump(buf, r);
                fflush(stdout);
            }
        }
        struct timespec ts = {0, 5 * 1000 * 1000};
        nanosleep(&ts, NULL);
    }
    for (int i = 0; i < n; i++)
        hid_close(h[i]);
    return 0;
}

/* Build a "CRT\0\0" packet from offset=value pairs; values are hex bytes
 * ("0a1b") or, if not valid hex, ASCII text ("STP"). */
static int cmd_raw(ss551_device *dev, int argc, char **argv)
{
    uint8_t p[SS551_OUT_PACKET] = {'C', 'R', 'T', 0, 0};
    int last = 4;
    for (int i = 0; i < argc; i++) {
        char *eq = strchr(argv[i], '=');
        if (!eq)
            return 2;
        int off = atoi(argv[i]);
        const char *v = eq + 1;
        size_t n = strlen(v);
        int hex = n % 2 == 0 && n > 0 && strspn(v, "0123456789abcdefABCDEF") == n;
        for (size_t k = 0; hex ? k < n / 2 : k < n; k++) {
            if (off + (int)k >= SS551_OUT_PACKET)
                return 2;
            unsigned b = (unsigned char)v[k];
            if (hex)
                sscanf(v + 2 * k, "%2x", &b);
            p[off + k] = (uint8_t)b;
            if (off + (int)k > last)
                last = off + (int)k;
        }
    }
    printf("send: ");
    dump(p, last + 1);
    return ss551_send_raw(dev, p, sizeof p) ? 1 : 0;
}

static int cmd_image(ss551_device *dev, int key, const char *path)
{
    FILE *f = fopen(path, "rb");
    if (!f) {
        perror(path);
        return 1;
    }
    fseek(f, 0, SEEK_END);
    long len = ftell(f);
    fseek(f, 0, SEEK_SET);
    uint8_t *buf = malloc(len);
    if (fread(buf, 1, len, f) != (size_t)len) {
        perror("read");
        fclose(f);
        free(buf);
        return 1;
    }
    fclose(f);
    int rc = ss551_start_session(dev) || ss551_set_key_jpeg(dev, (uint8_t)key, buf, len) ||
             ss551_flush(dev);
    free(buf);
    return rc ? 1 : 0;
}

int main(int argc, char **argv)
{
    if (argc < 2) {
        usage();
        return 2;
    }
    if (ss551_init() != 0) {
        fprintf(stderr, "hid_init failed\n");
        return 1;
    }
    if (!strcmp(argv[1], "rawlisten")) {
        int rc = cmd_rawlisten();
        ss551_exit();
        return rc;
    }
    ss551_device *dev = ss551_open(NULL);
    if (!dev) {
        fprintf(stderr, "%s\n", ss551_error(NULL));
        ss551_exit();
        return 1;
    }

    const char *cmd = argv[1];
    int rc = 0;
    if (!strcmp(cmd, "info")) {
        char serial[64];
        ss551_get_serial(dev, serial, sizeof serial);
        printf("serial: %s\n", serial);
    } else if (!strcmp(cmd, "listen")) {
        rc = cmd_listen(dev);
    } else if (!strcmp(cmd, "brightness") && argc > 2) {
        rc = ss551_set_brightness(dev, atoi(argv[2]));
    } else if (!strcmp(cmd, "wake")) {
        rc = ss551_wake_screen(dev);
    } else if (!strcmp(cmd, "off")) {
        rc = ss551_screen_off(dev);
    } else if (!strcmp(cmd, "clear")) {
        int key = argc > 2 ? (int)strtol(argv[2], NULL, 0) : SS551_KEY_ALL;
        rc = ss551_clear_key(dev, (uint8_t)key) || ss551_flush(dev);
    } else if (!strcmp(cmd, "raw") && argc > 2) {
        rc = cmd_raw(dev, argc - 2, argv + 2);
        if (rc == 2)
            usage();
    } else if (!strcmp(cmd, "image") && argc > 3) {
        rc = cmd_image(dev, (int)strtol(argv[2], NULL, 0), argv[3]);
    } else {
        usage();
        rc = 2;
    }
    if (rc && rc != 2)
        fprintf(stderr, "error: %s\n", ss551_error(dev));

    ss551_close(dev);
    ss551_exit();
    return rc ? 1 : 0;
}
