# P&K Applications branding

Shared branding for the Google TV receiver and Windows sender. The white P&K lettering sits inside a teal display on a navy rounded tile; the full company name appears on the TV banner and app publisher labels.

## Artwork and platform exports

- `pk-applications-icon.png`: original generated transparent square icon (1254 x 1254).
- `pk-screen-receiver-banner.png`: original generated company/app banner (1672 x 941).
- `pk-applications.ico`: Windows icon with 16, 20, 24, 32, 40, 48, 64, 96, 128 and 256 pixel PNG-backed frames.
- `Export-Icons.ps1`: converts the source artwork to platform sizes without changing the design.
- Android `ic_launcher_pk.png`: 48/72/96/144/192 pixel launcher assets in the corresponding mipmap density directories.
- Android `tv_banner_pk.png`: 320 x 180 launcher banner.

The Android manifest references the new assets; original launcher artwork is retained. Both Windows executables use the ICO; Projector Connect also embeds it for its title bar/taskbar and displays the full company name with the ampersand rendered literally.

To regenerate the format/size exports from these source images, run from the repository root on Windows:

```powershell
.\assets\branding\Export-Icons.ps1
```

The source artwork was created using the built-in image-generation tool, without the API/CLI fallback. Exporting only resizes and packages those images. No credentials, machine paths, device identifiers or runtime captures are part of these assets.

## Final prompts

### Icon

Use case: logo-brand. Asset type: production app icon shared by a Google TV screen receiver and a Windows projector connection app, owned by P&K Applications. Create ONE square icon, straight-on flat digital artwork, not a mockup or presentation board. Use the existing app palette: deep midnight navy (#0E151F), mint teal (#5ED4C0), and crisp white. A premium minimal rounded-square navy tile with genuinely transparent space outside its smoothly rounded corners. Inside, one bold teal outline of a widescreen display, with a short simple centered stand; integrate the exact lettering "P&K" in large, heavy, exceptionally legible white geometric sans-serif in the screen. The lettering is the main identifying feature, not a small caption. Spell exactly P ampersand K. Large balanced shapes, generous deliberate negative space, no fine details. Keep all lettering and the display silhouette in the central safe area so the symbol remains recognizable under circular/squircle launcher masks. The tile itself nearly fills the canvas. Communicate screen sharing and company ownership through this monogram and display. Flat clean edges with excellent contrast, sharp professional finish. No extra text, no gradients, no photography, no 3D, no bevels, no drop shadow, no watermark, no background scene. Produce a high-resolution square source image suitable for PNG app icons and a multi-resolution Windows ICO.

### TV banner

The reference image is `pk-applications-icon.png`.

Use case: logo-brand. Asset type: finished Android TV / Google TV launcher banner, ONE wide rectangular 16:9 image, not a mockup. Image 1 is a visual reference: reuse its exact P&K screen icon, its navy/teal/white identity and clean bold typography. Produce a matching, beautifully balanced brand banner with an uninterrupted deep navy (#0E151F) full-bleed background. A clearly recognizable version of the supplied P&K screen icon occupies the left portion; to its right, set the exact company name "P&K Applications" as the main large white headline, with the exact app name "Screen Receiver" on a smaller mint-teal line beneath. Spell precisely P ampersand K, capital A in Applications, and capital S and R in Screen Receiver. Those are the only two text lines outside the P&K monogram. Keep everything comfortably inside the edges, with strong negative space and large, simple, clear letters that remain readable when this artwork is scaled down to a 320x180-pixel TV launcher tile. Horizontally composed 16:9 canvas, avoid excess empty border. Premium minimal flat digital graphic, crisp, cohesive with the supplied icon. Do not add fine decorative lines, extra symbols, 3D effects, device mockups, extra slogans, watermarks, or a transparent background.
