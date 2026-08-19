from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
EYE = ROOT / "EyeCare"


def require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        raise AssertionError(f"{label}: missing {needle!r}")


def main() -> None:
    filter_cs = (EYE / "Pages" / "FilterPage.xaml.cs").read_text(encoding="utf-8")
    settings_cs = (EYE / "Models" / "AppSettings.cs").read_text(encoding="utf-8")

    # Every value-changed handler must be inert while XAML is initializing.
    for handler in (
        "TempSlider_ValueChanged",
        "StrengthSlider_ValueChanged",
        "BrightnessSlider_ValueChanged",
    ):
        start = filter_cs.index(f"private void {handler}")
        end = filter_cs.find("\n    private void ", start + 1)
        body = filter_cs[start:] if end < 0 else filter_cs[start:end]
        require(body, "if (_loading) return;", handler)

    # A newly enabled brightness filter must have a visible non-100% default.
    require(settings_cs, "Brightness { get; set; } = 0.85", "brightness default")

    # All pages must center their bounded content rather than pinning it left.
    for page in ("OverviewPage", "FilterPage", "BreakPage", "SettingsPage"):
        xaml = (EYE / "Pages" / f"{page}.xaml").read_text(encoding="utf-8")
        require(xaml, 'HorizontalAlignment="Center"', page)
        content_region = xaml.split("<ScrollViewer", 1)[1].split("</ScrollViewer>", 1)[0]
        content_tag = content_region.split("<StackPanel", 1)[1].split(">", 1)[0]
        if 'HorizontalAlignment="Left"' in content_tag:
            raise AssertionError(f"{page}: page content is still left-aligned")

    print("UI regression checks passed")


if __name__ == "__main__":
    main()
