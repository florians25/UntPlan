"""Erzeugt das App-Symbol build/icon.png (256x256) ohne externe Abhängigkeiten."""
import struct
import zlib

W = H = 256


def px(x, y):
    r = 48
    cx = min(max(x, r), W - 1 - r)
    cy = min(max(y, r), H - 1 - r)
    if (x - cx) ** 2 + (y - cy) ** 2 > r * r:
        return (0, 0, 0, 0)
    # Buchstabe "U"
    if (70 <= x <= 105 or 151 <= x <= 186) and 60 <= y <= 150:
        return (255, 255, 255, 255)
    if y > 150 and ((x - 128) ** 2) / 58**2 + ((y - 150) ** 2) / 46**2 <= 1 and not (((x - 128) ** 2) / 23**2 + ((y - 150) ** 2) / 14**2 <= 1):
        return (255, 255, 255, 255)
    t = (x + y) / (2 * W)
    return (int(59 * (1 - t) + 34 * t), int(130 * (1 - t) + 197 * t), int(246 * (1 - t) + 94 * t), 255)


raw = b"".join(b"\x00" + b"".join(bytes(px(x, y)) for x in range(W)) for y in range(H))


def chunk(t, d):
    return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)


png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", W, H, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
with open("build/icon.png", "wb") as f:
    f.write(png)
