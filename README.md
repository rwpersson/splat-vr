# Splat VR

Photorealistic Gaussian-splat rooms on a standalone **Meta Quest 3S**, made **playable automatically**: scan a room with a phone, click once, and about fifteen seconds later it's a world you can walk around, pick things up in, and throw them at the furniture.

Portfolio page with video and pictures: https://rwpersson.github.io/portfolio/projects/splat-vr.html

## What it does

**In the headset** (Unity 6 · OpenXR · URP, no Meta SDK):
- Gaussian-splat rooms at 72 fps on a phone-class chip (splat budget set from measurements)
- Grab, drop and throw a scanned object (the mug is a splat too), with physics against the room
- The room hides objects behind it: an occlusion pre-pass patched into the open-source renderer
- Real controller models from the headset (`XR_FB_render_model`), smooth locomotion

**In the editor, one click per scan** (`Tools → SplatVR → Import Scan…`):
1. Find the scan's dense core and crop away the background shell
2. Thin it to the headset budget (100k splats)
3. Import it, flip it to Unity's axes, and set it on the floor
4. Build colliders from the splats themselves: a collision mesh from the solid splats (via [splat-transform](https://github.com/playcanvas/splat-transform)) and a floor found from where the splats pile up
5. Place objects on flat, free surfaces (`Tools → SplatVR → Place Objects`)

Tested on a second scan it had never seen, with unchanged settings.

## Where to look

- `SplatVR/Assets/Editor/`: the automatic pipeline (`SplatScanImporter`, `SplatColliderGenerator`, `SplatObjectPlacer`)
- `SplatVR/Packages/net.kleinbeck.gaussians/`: embedded copy of [Unity Gaussians](https://github.com/ninjamode/Unity-VR-Gaussian-Splatting) (MIT, by Constantin Kleinbeck) with small `SplatVR patch` edits (grep for them)
- `SplatVR/Assets/Scripts/`: grabbing and throwing, occlusion and collider toggles, controller models, locomotion
- Scenes: `Couch` (a second scan, imported fully automatically) and `Interaction` (the desk)

Scan files (`.ply`) and generated splat assets aren't in the repo (size); the collision meshes are.

## Status

Research prototype. Next: running AI-generated worlds (World Labs Marble / Atlas) through the same pipeline.
