from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "assets"
SIZE = 1024


def blend_color(start, end, amount):
    return tuple(
        round(start[index] + (end[index] - start[index]) * amount)
        for index in range(3)
    )


def make_icon():
    image = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    mask = Image.new("L", (SIZE, SIZE), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        (18, 18, SIZE - 18, SIZE - 18), radius=220, fill=255
    )

    gradient = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(gradient)
    start = (59, 130, 246)
    end = (29, 78, 216)
    for y in range(SIZE):
        color = blend_color(start, end, y / SIZE)
        draw.line((0, y, SIZE, y), fill=color + (255,))
    image.paste(gradient, (0, 0), mask)

    layer = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    shadow = ImageDraw.Draw(layer)
    shadow.rounded_rectangle(
        (216, 188, 812, 886), radius=82, fill=(10, 30, 80, 54)
    )
    shadow.rounded_rectangle(
        (206, 168, 806, 856), radius=84, fill=(255, 255, 255, 255)
    )
    shadow.rounded_rectangle(
        (214, 176, 798, 848), radius=76, outline=(216, 229, 247, 255), width=12
    )

    font_path = Path("C:/Windows/Fonts/msyh.ttc")
    if font_path.exists():
        font = ImageFont.truetype(str(font_path), 430)
    else:
        font = ImageFont.load_default()
    shadow.text((506, 438), "名", font=font, fill=(37, 99, 235, 255), anchor="mm")

    check = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    check_draw = ImageDraw.Draw(check)
    check_draw.ellipse((674, 650, 906, 882), fill=(22, 163, 74, 255))
    check_draw.ellipse((674, 650, 906, 882), outline=(255, 255, 255, 255), width=20)
    check_draw.line((720, 766, 766, 812, 858, 714), fill=(255, 255, 255, 255), width=42)
    layer.alpha_composite(check)

    image.alpha_composite(layer)
    return image


def main():
    ASSETS.mkdir(exist_ok=True)
    image = make_icon()
    png_path = ASSETS / "app-icon.png"
    image.save(png_path, "PNG")
    ico_path = ASSETS / "app-icon.ico"
    image.save(
        ico_path,
        "ICO",
        sizes=[
            (16, 16),
            (24, 24),
            (32, 32),
            (48, 48),
            (64, 64),
            (128, 128),
            (256, 256),
        ],
    )
    print(png_path)
    print(ico_path)


if __name__ == "__main__":
    main()
