"""生成护眼软件图标 eye.ico（纯 Python，无第三方依赖）。
32x32 + 16x16 两张 32 位 BGRA 图像，含 alpha 通道。"""
import struct, os

OUT = os.path.join(os.path.dirname(__file__), "..", "EyeCare", "Assets", "eye.ico")
os.makedirs(os.path.dirname(OUT), exist_ok=True)

def make_eye_pixels(size):
    """绘制护眼主题眼睛图标：暖琥珀色圆环 + 深色瞳孔。返回 BGRA 字节（自底向上）。"""
    px = [[(0, 0, 0, 0) for _ in range(size)] for _ in range(size)]
    cx = cy = (size - 1) / 2.0
    R = size / 2.0 - 1.0
    for y in range(size):
        for x in range(size):
            dx = (x - cx) / R
            dy = (y - cy) / R
            d = (dx * dx + dy * dy) ** 0.5
            if d <= 1.0:
                # 眼白（淡暖色）
                base = (255, 246, 235, 255)
            else:
                continue
            # 虹膜圆形（琥珀色）
            iris = 0.62
            if d <= iris:
                # 渐变琥珀
                t = d / iris
                base = (255, int(160 + 60 * t), int(40 + 120 * t), 255)
            pupil = 0.28
            if d <= pupil:
                base = (30, 30, 45, 255)
                # 高光
                hx = (x - cx) / R
                hy = (y - cy) / R
                hd = ((hx + 0.09) ** 2 + (hy + 0.09) ** 2) ** 0.5
                if hd <= 0.10:
                    base = (255, 255, 255, 255)
            px[y][x] = base
    # 自底向上写入（ICO 格式要求）
    out = bytearray()
    for y in range(size - 1, -1, -1):
        for x in range(size):
            b, g, r, a = px[y][x]
            out += bytes((b, g, r, a))
    return bytes(out)

def make_ico():
    images = [make_eye_pixels(32), make_eye_pixels(16)]
    count = len(images)
    # ICO 文件头
    header = struct.pack("<HHH", 0, 1, count)
    entries = b""
    offset = 6 + 16 * count
    blobs = []
    for size, data in zip((32, 16), images):
        # BITMAPINFOHEADER
        hdr = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, len(data), 0, 0, 0, 0)
        and_mask = bytes(size * 4)  # 4 字节对齐每行
        blob = hdr + data + and_mask
        blobs.append(blob)
        entries += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
    return header + entries + b"".join(blobs)

data = make_ico()
with open(OUT, "wb") as f:
    f.write(data)
print("Wrote", OUT, f"{len(data)} bytes")