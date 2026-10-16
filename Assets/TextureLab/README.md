# Texture Lab for Unity Editor

`Assets/TextureLab` is the complete importable Unity Editor product. It contains the shared processing runtime, built-in palettes and presets, shaders, and the Unity Editor UI.

The standalone application's Runtime UI, scenes, configuration and platform adapters live separately under `Assets/TextureLabApp` and are not part of the Unity Editor package.

## Exporting a .unitypackage

1. Select `Assets/TextureLab` in the Project window.
2. Choose `Assets > Export Package...`.
3. Keep the contents of `Assets/TextureLab` selected.
4. Disable `Include dependencies`.
5. Save the resulting `.unitypackage` outside `Assets`, for example under `Builds/Packages`.

After importing, open the tool from `Tools > Texture Lab > Open Window`.
