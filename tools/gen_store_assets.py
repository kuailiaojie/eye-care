"""
生成 Microsoft Store 所需的图标资源（PNG）。
从 eye.ico 提取并缩放到 Store 要求的各种尺寸。

用法：python tools/gen_store_assets.py
"""

import os
import sys

try:
    from PIL import Image, ImageDraw, ImageFilter
except ImportError:
    print("Installing Pillow...")
    import subprocess
    subprocess.check_call([sys.executable, "-m", "pip", "install", "Pillow"])
    from PIL import Image, ImageDraw

ICO_PATH = os.path.join(os.path.dirname(__file__), "..", "EyeCare", "Assets", "eye.ico")
OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "store")
EYE_SRC = os.path.join(os.path.dirname(__file__), "..", "EyeCare", "Assets", "eye.ico")

# Store 要求的图标尺寸
SIZES = {
    "StoreLogo.png": (50, 50),
    "Square44x44Logo.png": (44, 44),
    "Square150x150Logo.png": (150, 150),
    "Square310x310Logo.png": (310, 310),
    "Wide310x150Logo.png": (310, 150),
    "SplashScreen.png": (620, 300),
    "Banner.png": (1200, 300),
}


def make_icon(size):
    """从 ICO 提取最大尺寸图标，缩放到目标。"""
    ico = Image.open(ICO_PATH)
    # 取最大尺寸
    ico.size = max(ico.ico.sizes()) if hasattr(ico, "ico") else ico.size
    ico = ico.convert("RGBA")
    return ico.resize((size, size), Image.LANCZOS)


def make_wide(w, h):
    """宽幅图标：图标居左，右侧留白。"""
    icon = make_icon(min(h, h))
    canvas = Image.new("RGBA", (w, h), (30, 30, 60, 255))
    # 居中放置图标
    px = (w - h) // 2
    canvas.paste(icon, (px, 0), icon)
    return canvas


def make_splash(w, h):
    """启动画面：居中图标 + 深蓝背景。"""
    icon = make_icon(h // 2)
    canvas = Image.new("RGBA", (w, h), (30, 30, 60, 255))
    px = (w - icon.width) // 2
    py = (h - icon.height) // 2
    canvas.paste(icon, (px, py), icon)
    return canvas


def make_banner(w, h):
    """Store 横幅：图标居左 + 文字区域。"""
    icon = make_icon(h - 40)
    canvas = Image.new("RGBA", (w, h), (30, 30, 60, 255))
    canvas.paste(icon, (60, 20), icon)
    return canvas


def main():
    os.makedirs(OUT_DIR, exist_ok=True)

    for name, (w, h) in SIZES.items():
        out_path = os.path.join(OUT_DIR, name)
        if w == h:
            img = make_icon(w)
        elif name == "SplashScreen.png":
            img = make_splash(w, h)
        elif name == "Banner.png":
            img = make_banner(w, h)
        else:
            img = make_wide(w, h)
        img.save(out_path, "PNG")
        print(f"  {name}: {w}x{h} OK")

    print(f"\n✅ Store assets generated in: {OUT_DIR}")


if __name__ == "__main__":
    main()
