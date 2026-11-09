"""Check bundled theme text against composited surfaces and interactive states (no dependencies)."""
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
KEY = "{http://schemas.microsoft.com/winfx/2006/xaml}Key"

def color(value):
    value = value.lstrip("#")
    channels = [int(value[i:i+2], 16) / 255 for i in range(0, len(value), 2)]
    return channels if len(channels) == 4 else [1] + channels

def over(front, back):
    return [1] + [front[i]*front[0] + back[i]*(1-front[0]) for i in (1, 2, 3)]

def luminance(value):
    return sum(weight*(v/12.92 if v <= .04045 else ((v+.055)/1.055)**2.4)
               for weight, v in zip((.2126, .7152, .0722), value[1:]))

def contrast(first, second):
    low, high = sorted((luminance(first), luminance(second)))
    return (high+.05)/(low+.05)

def main():
    failures, checks, themes = [], 0, 0
    expected_keys = None
    for path in sorted((ROOT / "Themes").glob("*.xaml")):
        brushes = {e.get(KEY): color(e.get("Color")) for e in ET.parse(path).getroot()
                   if e.tag.endswith("SolidColorBrush")}
        if "Brush.Window" not in brushes:
            continue
        themes += 1
        if expected_keys is None:
            expected_keys = set(brushes)
        elif set(brushes) != expected_keys:
            failures.append(f"{path.stem}: theme brush keys differ")
        for surface in ("Window", "Card", "Input", "Raised", "Surface"):
            base = over(brushes["Brush." + surface], brushes["Brush.Window"])
            for state in (None, "Hover", "Pressed", "AccentSoft"):
                background = over(brushes["Brush."+state], base) if state else base
                for role in ("Text", "TextMuted", "TextDim", "AccentText", "Danger", "Warning"):
                    ratio = contrast(brushes["Brush."+role], background)
                    checks += 1
                    if ratio < 4.5:
                        failures.append(f"{path.stem}: {role} on {surface}/{state or 'normal'}: {ratio:.2f}:1")
        checks += 1
        if contrast(brushes["Brush.OnAccent"], brushes["Brush.Accent"]) < 4.5:
            failures.append(f"{path.stem}: text on accent fill has insufficient contrast")
    if failures:
        raise SystemExit("\n".join(failures))
    print(f"Passed {checks} contrast checks across {themes} themes, including hover, pressed and selected surfaces.")

if __name__ == "__main__":
    main()
