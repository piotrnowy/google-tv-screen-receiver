# P&K Applications branding

Both applications use artwork adapted from the supplied CompanyLogo.png: navy P&K lettering with the original blue/cyan identity on white. The former teal display icon has been replaced. The original supplied file remains unchanged.

## Source and exports

- company-logo.png: byte-for-byte copy of the supplied company logo, retained as the authoritative visual reference.
- pk-applications-icon.png: square icon master adapted from that logo. The P&K monogram is prominent; the small APPLICATIONS subline is omitted for icon-size readability.
- pk-screen-receiver-banner.png: wide Google TV banner master with the company wordmark and Screen Receiver title.
- pk-applications.ico: Windows icon with 16, 20, 24, 32, 40, 48, 64, 96, 128 and 256 pixel PNG-backed frames.
- Export-Icons.ps1: deterministic resizing/packaging of the approved masters.
- Android ic_launcher_pk.png: 48/72/96/144/192 pixel launcher resources.
- Android tv_banner_pk.png: 320x180 launcher banner.

The Android manifest already selects these resource names. Both Windows executables use the ICO; Projector Connect additionally embeds it for its title bar/taskbar. Existing publisher text and runtime behavior are unchanged.

Run .\assets\branding\Export-Icons.ps1 from repository root to regenerate the platform formats from the masters.

The adapted masters were created with the built-in image-generation tool, using the supplied company logo as the edit target. They are visual adaptations, not pixel-identical copies of the original. No API key or CLI fallback was used. Platform exports only resize/package these masters. Original generated artwork is recoverable from Git history.

## Final edit prompts

### Icon

Use case: logo-brand / precise-object-edit. Edit target: the supplied CompanyLogo.png, the user's official P&K Applications logo. Create a production square app icon master for Windows and Android, with a solid pure-white background and the original P&K monogram centered, large and legible. Remove the excessive exterior whitespace and omit ONLY the small APPLICATIONS subline for tiny-size legibility. Preserve the exact custom shapes of the P, ampersand and K, the dark navy letters, and the existing cyan/electric-blue gradient in the ampersand and K wing. Do not redesign the mark. Fit the complete P&K monogram within the central 82% width, with balanced white padding; no cropping of letters. No display frame, no invented symbol, no extra text, no mockup, no shadows, no neon, no tile perspective. Output one square high-resolution icon source.

### TV banner

Use case: logo-brand / precise-object-edit. Edit target: the supplied CompanyLogo.png, the user's official logo. Create one finished 16:9 Google TV launcher banner with pure white background. Preserve and reproduce the supplied P&K monogram and APPLICATIONS wordmark exactly, including navy letters and cyan/electric-blue gradient shapes; do not redesign or replace its typography. Remove the excessive exterior whitespace so the complete original logo occupies about 82% of the banner width, centered and balanced. Below the APPLICATIONS wordmark add only the app title "Screen Receiver" in a simple readable navy sans-serif, with comfortable vertical separation. All artwork and text must remain inside a 7% safe margin and readable at 320x180. No other symbols, no display frame, no mockup, no decoration or shadows. Output one wide high-resolution banner source.
