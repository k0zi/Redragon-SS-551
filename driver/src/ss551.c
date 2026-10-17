#include "ss551.h"

#include <hidapi.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>

struct ss551_device {
    hid_device *hid;
    char serial[64];
    char error[256];
};

static char g_error[256];

static void set_error(ss551_device *dev, const char *what)
{
    char *dst = dev ? dev->error : g_error;
    const wchar_t *werr = hid_error(dev ? dev->hid : NULL);
    if (werr)
        snprintf(dst, sizeof(dev->error), "%s: %ls", what, werr);
    else
        snprintf(dst, sizeof(dev->error), "%s", what);
}

int ss551_init(void) { return hid_init(); }

void ss551_exit(void) { hid_exit(); }

const char *ss551_error(ss551_device *dev) { return dev ? dev->error : g_error; }

ss551_device *ss551_open(const char *serial)
{
    struct hid_device_info *list = hid_enumerate(SS551_VID, SS551_PID);
    struct hid_device_info *pick = NULL;
    for (struct hid_device_info *it = list; it; it = it->next) {
        /* Interface 0 is the vendor channel; interface 1 is a boot keyboard. */
        if (it->interface_number != 0 && it->usage_page != 0xFFA0)
            continue;
        if (serial && it->serial_number) {
            char s[64];
            snprintf(s, sizeof s, "%ls", it->serial_number);
            if (strcmp(s, serial) != 0)
                continue;
        }
        pick = it;
        break;
    }
    if (!pick) {
        hid_free_enumeration(list);
        snprintf(g_error, sizeof g_error, "no SS-551 found (check udev rule / permissions)");
        return NULL;
    }

    ss551_device *dev = calloc(1, sizeof *dev);
    dev->hid = hid_open_path(pick->path);
    if (pick->serial_number)
        snprintf(dev->serial, sizeof dev->serial, "%ls", pick->serial_number);
    hid_free_enumeration(list);
    if (!dev->hid) {
        set_error(NULL, "hid_open_path failed");
        free(dev);
        return NULL;
    }
    return dev;
}

void ss551_close(ss551_device *dev)
{
    if (!dev)
        return;
    hid_close(dev->hid);
    free(dev);
}

int ss551_get_serial(ss551_device *dev, char *buf, size_t len)
{
    snprintf(buf, len, "%s", dev->serial);
    return 0;
}

int ss551_get_firmware(ss551_device *dev, char *buf, size_t len)
{
    uint8_t rep[SS551_IN_PACKET + 1] = {0};
    int n = hid_get_input_report(dev->hid, rep, sizeof rep);
    if (n <= 1) {
        set_error(dev, "hid_get_input_report failed");
        return -1;
    }
    /* rep[0] is the report ID; the rest is a NUL-padded ASCII string. */
    snprintf(buf, len, "%.*s", n - 1, (const char *)rep + 1);
    return 0;
}

/* One output report: report ID 0 followed by a zero-padded payload. */
static int write_packet(ss551_device *dev, const uint8_t *payload, size_t len)
{
    uint8_t buf[SS551_OUT_PACKET + 1];
    if (len > SS551_OUT_PACKET)
        len = SS551_OUT_PACKET;
    memset(buf, 0, sizeof buf);
    memcpy(buf + 1, payload, len);
    if (hid_write(dev->hid, buf, sizeof buf) < 0) {
        set_error(dev, "hid_write failed");
        return -1;
    }
    return 0;
}

int ss551_send_raw(ss551_device *dev, const uint8_t *payload, size_t len)
{
    return write_packet(dev, payload, len);
}

/* "CRT\0\0" + 3..7 letter command, optional bytes placed at absolute offsets. */
static int send_cmd(ss551_device *dev, const char *cmd, int off, const uint8_t *arg, size_t arglen)
{
    uint8_t p[SS551_OUT_PACKET];
    memset(p, 0, sizeof p);
    memcpy(p, "CRT", 3);
    memcpy(p + 5, cmd, strlen(cmd));
    if (arg)
        memcpy(p + off, arg, arglen);
    return write_packet(dev, p, sizeof p);
}

int ss551_wake_screen(ss551_device *dev)
{
    const uint8_t on = 1;
    return send_cmd(dev, "DIS", 10, &on, 1);
}

int ss551_screen_off(ss551_device *dev)
{
    const uint8_t on = 1;
    return send_cmd(dev, "HAN", 10, &on, 1);
}

int ss551_set_brightness(ss551_device *dev, int pct)
{
    if (pct < 0)
        pct = 0;
    if (pct > 100)
        pct = 100;
    const uint8_t v = (uint8_t)pct;
    return send_cmd(dev, "LIG", 10, &v, 1);
}

int ss551_clear_key(ss551_device *dev, uint8_t key)
{
    return send_cmd(dev, "CLE", 11, &key, 1);
}

int ss551_flush(ss551_device *dev) { return send_cmd(dev, "STP", 0, NULL, 0); }

int ss551_heartbeat(ss551_device *dev) { return send_cmd(dev, "CONNECT", 0, NULL, 0); }

int ss551_send_qucmd(ss551_device *dev, const uint8_t params[6])
{
    return send_cmd(dev, "QUCMD", 10, params, 6);
}

int ss551_start_session(ss551_device *dev)
{
    /* Values the vendor app sends right after opening the device
     * (SDDeviceDisplayWidget, sddevicedisplaywidget.cpp). */
    static const uint8_t qucmd[6] = {0x11, 0x11, 0x00, 0x11, 0x00, 0x11};
    if (ss551_send_qucmd(dev, qucmd) < 0)
        return -1;
    return ss551_wake_screen(dev);
}

int ss551_set_key_jpeg(ss551_device *dev, uint8_t key, const uint8_t *jpeg, size_t len)
{
    /* Header: size as big-endian u32 at [8..11], key index at [12]. */
    uint8_t hdr[5] = {
        (uint8_t)(len >> 24), (uint8_t)(len >> 16), (uint8_t)(len >> 8), (uint8_t)len, key,
    };
    if (send_cmd(dev, "BAT", 8, hdr, sizeof hdr) < 0)
        return -1;
    for (size_t off = 0; off < len; off += SS551_OUT_PACKET) {
        size_t n = len - off < SS551_OUT_PACKET ? len - off : SS551_OUT_PACKET;
        if (write_packet(dev, jpeg + off, n) < 0)
            return -1;
    }
    return 0;
}

int ss551_read_event(ss551_device *dev, ss551_event *ev, int timeout_ms)
{
    memset(ev, 0, sizeof *ev);
    int n = hid_read_timeout(dev->hid, ev->raw, sizeof ev->raw, timeout_ms);
    if (n < 0) {
        set_error(dev, "hid_read failed");
        return -1;
    }
    if (n == 0)
        return 0;
    ev->raw_len = n;
    const uint8_t *r = ev->raw;
    if (n >= 11 && memcmp(r, "ACK", 3) == 0 && r[5] == 'O' && r[6] == 'K') {
        ev->key = r[9];
        ev->state = r[10];
        ev->type = ev->key ? SS551_EVENT_KEY : SS551_EVENT_ACK;
    } else if (n >= 11 && memcmp(r, "CRT", 3) == 0 && memcmp(r + 5, "MOD", 3) == 0) {
        ev->type = SS551_EVENT_MODE;
        ev->state = r[10];
    } else {
        ev->type = SS551_EVENT_OTHER;
    }
    return 1;
}
