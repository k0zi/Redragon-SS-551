#include "ss551.h"

#include <fcntl.h>
#include <linux/uinput.h>
#include <stdlib.h>
#include <string.h>
#include <sys/ioctl.h>
#include <time.h>
#include <unistd.h>

struct ss551_vkbd {
    int fd;
};

static int emit(int fd, uint16_t type, uint16_t code, int32_t value)
{
    struct input_event ev;
    memset(&ev, 0, sizeof ev);
    ev.type = type;
    ev.code = code;
    ev.value = value;
    return write(fd, &ev, sizeof ev) == (ssize_t)sizeof ev ? 0 : -1;
}

static void sleep_ms(int ms)
{
    struct timespec ts = {0, ms * 1000L * 1000L};
    nanosleep(&ts, NULL);
}

ss551_vkbd *ss551_vkbd_open(void)
{
    int fd = open("/dev/uinput", O_WRONLY | O_NONBLOCK | O_CLOEXEC);
    if (fd < 0)
        return NULL;

    ioctl(fd, UI_SET_EVBIT, EV_KEY);
    for (int code = 1; code < KEY_MAX; code++)
        ioctl(fd, UI_SET_KEYBIT, code);

    struct uinput_setup setup;
    memset(&setup, 0, sizeof setup);
    setup.id.bustype = BUS_VIRTUAL;
    setup.id.vendor = SS551_VID;
    setup.id.product = SS551_PID;
    strncpy(setup.name, "SS-551 virtual keyboard", UINPUT_MAX_NAME_SIZE - 1);
    if (ioctl(fd, UI_DEV_SETUP, &setup) < 0 || ioctl(fd, UI_DEV_CREATE) < 0) {
        close(fd);
        return NULL;
    }
    /* Give the compositor time to pick up the new device before first use. */
    sleep_ms(200);

    ss551_vkbd *kbd = malloc(sizeof *kbd);
    kbd->fd = fd;
    return kbd;
}

void ss551_vkbd_close(ss551_vkbd *kbd)
{
    if (!kbd)
        return;
    ioctl(kbd->fd, UI_DEV_DESTROY);
    close(kbd->fd);
    free(kbd);
}

int ss551_vkbd_tap(ss551_vkbd *kbd, const uint16_t *codes, int n)
{
    int rc = 0;
    for (int i = 0; i < n; i++)
        rc |= emit(kbd->fd, EV_KEY, codes[i], 1);
    rc |= emit(kbd->fd, EV_SYN, SYN_REPORT, 0);
    sleep_ms(10);
    for (int i = n - 1; i >= 0; i--)
        rc |= emit(kbd->fd, EV_KEY, codes[i], 0);
    rc |= emit(kbd->fd, EV_SYN, SYN_REPORT, 0);
    return rc;
}
