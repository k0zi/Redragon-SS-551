/*
 * libss551 - userspace driver for the Redragon SS-551 stream deck
 * (Mirabox "Stream Dock" N3-family hardware, USB 0200:2000).
 *
 * The device speaks a vendor HID protocol on interface 0: every host->device
 * command is a 1024-byte output report starting with "CRT\0\0", and every
 * device->host message is a 512-byte input report ("ACK\0\0OK\0\0..." for
 * key events). No kernel driver is needed; access goes through hidapi.
 */
#ifndef SS551_H
#define SS551_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#if defined(_WIN32)
#define SS551_API __declspec(dllexport)
#else
#define SS551_API __attribute__((visibility("default")))
#endif

#define SS551_VID 0x0200
#define SS551_PID 0x2000

#define SS551_OUT_PACKET 1024 /* payload size of one output report */
#define SS551_IN_PACKET 512   /* size of one input report */

#define SS551_KEY_ALL 0xFF

typedef struct ss551_device ss551_device;

typedef enum ss551_event_type {
    SS551_EVENT_NONE = 0,  /* read timed out, nothing received */
    SS551_EVENT_KEY = 1,   /* key/knob event: key + state are valid */
    SS551_EVENT_ACK = 2,   /* plain "ACK..OK" with key == 0 (e.g. wake-up) */
    SS551_EVENT_MODE = 3,  /* "CRT..MOD": device changed mode, state = mode */
    SS551_EVENT_OTHER = 4, /* anything else; inspect raw */
} ss551_event_type;

typedef struct ss551_event {
    ss551_event_type type;
    uint8_t key;   /* byte 9 of the report */
    uint8_t state; /* byte 10 of the report */
    uint8_t raw[SS551_IN_PACKET];
    int raw_len;
} ss551_event;

/* Library lifetime (wraps hid_init / hid_exit). Return 0 on success. */
SS551_API int ss551_init(void);
SS551_API void ss551_exit(void);

/* Open the first SS-551 (serial == NULL) or a specific one by serial.
 * Picks the vendor interface (0) automatically. NULL on failure. */
SS551_API ss551_device *ss551_open(const char *serial);
SS551_API void ss551_close(ss551_device *dev);

/* Last error message for this device (or the global one if dev == NULL). */
SS551_API const char *ss551_error(ss551_device *dev);

SS551_API int ss551_get_serial(ss551_device *dev, char *buf, size_t len);

/* Firmware version string, e.g. "V3.SS-551.02.008". Returns 0 on success. */
SS551_API int ss551_get_firmware(ss551_device *dev, char *buf, size_t len);

/* Commands. All return 0 on success, -1 on error. */
SS551_API int ss551_wake_screen(ss551_device *dev);              /* DIS */
SS551_API int ss551_screen_off(ss551_device *dev);               /* HAN */
SS551_API int ss551_set_brightness(ss551_device *dev, int pct);  /* LIG, 0..100 */
SS551_API int ss551_clear_key(ss551_device *dev, uint8_t key);   /* CLE, SS551_KEY_ALL = all */
SS551_API int ss551_flush(ss551_device *dev);                    /* STP: show uploaded images */
SS551_API int ss551_heartbeat(ss551_device *dev);                /* CONNECT */

/* QUCMD settings packet: 6 parameter bytes at payload offset 10. Exact
 * meaning is unverified; the vendor app sends 11 11 00 11 00 11 on connect. */
SS551_API int ss551_send_qucmd(ss551_device *dev, const uint8_t params[6]);

/* Initial sequence the vendor app sends after opening (QUCMD + DIS). */
SS551_API int ss551_start_session(ss551_device *dev);

/* Upload a JPEG to one key (BAT header + data chunks). Call ss551_flush()
 * afterwards to make the device display it. */
SS551_API int ss551_set_key_jpeg(ss551_device *dev, uint8_t key, const uint8_t *jpeg, size_t len);

/* Send an arbitrary command payload (padded to SS551_OUT_PACKET). */
SS551_API int ss551_send_raw(ss551_device *dev, const uint8_t *payload, size_t len);

/* Read one input report. timeout_ms < 0 blocks. Returns 1 if an event was
 * read, 0 on timeout, -1 on error (device unplugged, ...). */
SS551_API int ss551_read_event(ss551_device *dev, ss551_event *ev, int timeout_ms);

/*
 * Virtual keyboard (Linux uinput), used to turn deck input into key presses
 * and media keys. Needs write access to /dev/uinput. Key codes are Linux
 * input event codes (KEY_* from <linux/input-event-codes.h>).
 */
typedef struct ss551_vkbd ss551_vkbd;

SS551_API ss551_vkbd *ss551_vkbd_open(void);
SS551_API void ss551_vkbd_close(ss551_vkbd *kbd);

/* Press codes[0..n-1] in order, then release them in reverse order. */
SS551_API int ss551_vkbd_tap(ss551_vkbd *kbd, const uint16_t *codes, int n);

#ifdef __cplusplus
}
#endif

#endif
