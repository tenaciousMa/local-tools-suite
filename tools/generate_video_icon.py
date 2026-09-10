from pathlib import Path

from PIL import Image, ImageDraw


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
    start = (37, 99, 235)
    end = (13, 148, 136)
    for y in range(SIZE):
        color = blend_color(start, end, y / SIZE)
        draw.line((0, y, SIZE, y), fill=color + (255,))
    image.paste(gradient, (0, 0), mask)

    card_shadow = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    shadow = ImageDraw.Draw(card_shadow)
    shadow.rounded_rectangle(
        (184, 218, 840, 842), radius=94, fill=(4, 18, 44, 64)
    )
    shadow.rounded_rectangle(
        (174, 198, 830, 822), radius=96, fill=(255, 255, 255, 255)
    )
    shadow.rounded_rectangle(
        (208, 232, 796, 788), radius=68, fill=(15, 23, 42, 255)
    )

    panes = (
        (232, 256, 495, 500, (59, 130, 246, 255)),
        (513, 256, 776, 500, (245, 158, 11, 255)),
        (232, 520, 495, 764, (20, 184, 166, 255)),
        (513, 520, 776, 764, (124, 58, 237, 255)),
    )
    for left, top, right, bottom, color in panes:
        shadow.rounded_rectangle(
            (left, top, right, bottom), radius=26, fill=color
        )

    shadow.rectangle((502, 236, 505, 784), fill=(255, 255, 255, 255))
    shadow.rectangle((214, 505, 790, 508), fill=(255, 255, 255, 255))
    shadow.ellipse((410, 406, 614, 610), fill=(15, 23, 42, 235))
    shadow.polygon(
        ((472, 446), (472, 570), (585, 508)),
        fill=(255, 255, 255, 255),
    )

    for x in range(242, 778, 56):
        shadow.rounded_rectangle(
            (x, 212, x + 26, 232), radius=8, fill=(219, 234, 254, 255)
        )
        shadow.rounded_rectangle(
            (x, 788, x + 26, 808), radius=8, fill=(219, 234, 254, 255)
        )

    image.alpha_composite(card_shadow)
    return image


def main():
    ASSETS.mkdir(exist_ok=True)
    image = make_icon()
    png_path = ASSETS / "video-icon.png"
    ico_path = ASSETS / "video-icon.ico"
    image.save(png_path, "PNG")
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
