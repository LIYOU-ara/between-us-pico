BETWEEN US - BLACK / RED CURVED COMPOSITE

Scene: Assets/BetweenUs/Composite/BetweenUs_BlackRed_CurvedComposite.unity
Unity menu: Prototype > Between Us > Black Red Curved Composite Studio

This is an independent desktop scene for post-production, with no headset or AI-service dependency.
It uses a perspective camera, five cylindrical glass surfaces and matching curved typography.
The palette is smoked black/grey with deep-red and coral flow, white type and a restrained silver edge.

Captures are written to CompositeCaptures in the Unity project folder:
- BetweenUs-Curved-BlackRed-Preview.png: 1920 x 1080 preview.
- BetweenUs-Curved-BlackRed-MotionCheck.png: a second animation time.
- BetweenUs-Curved-BlackRed-4K.png: 3840 x 2160 RGB on pure black.
- BetweenUs-Curved-Transparent-4K.png: 3840 x 2160 straight-alpha PNG.

In the studio, turn off Animate red flow to inspect a fixed Frame time.
Export 4K black + transparent PNG exports the displayed fixed-time composition.
Export 8s / 30fps PNG sequence creates SequenceBlack/frame-0000.png through frame-0239.png.
No camera motion is baked into that sequence; only the material moves.

Use the transparent PNG for normal alpha compositing. A black plate can also be keyed, but alpha is cleaner.
The exported PNG has straight/unassociated RGB, already converted from Unity's premultiplied rendering.

A black render cannot itself blur footage added later in an editing program.
For true frosted background in the final composite, duplicate your footage, blur it, and mask that blurred
copy by the panel alpha beneath the glass artwork. Alternatively, supply your footage as a texture to each
new Composite glass material's Optional compositing source plate slot and increase Use optional source plate.
The black-only preview uses a procedural frosted layer; the optional plate slot is zero by default.

All generated meshes, materials and scripts live in Assets/BetweenUs/Composite.
All text is ordinary editable TextMesh content in the scene hierarchy.
Existing headset scenes, their approved glass shaders and Android build settings are not changed.


TWO SEATED PEOPLE REVISION
The supplied OBJ pack includes two seated poses; these were extracted as independent mesh assets.
Both participants have fitted headsets, translucent chairs and silver luminous floor rings.
Headsets are children of the body mesh, so body scaling also moves the visor.
Move/scale the Participant parent to keep its chair and floor ring together.

The original camera and window transforms are retained. A separate full-frame export camera
fits the complete composition, including the raised quote panel and the new floor rings.
Original transform snapshots are in CompositeCaptures/SeatedPeople/WindowLayoutBefore.json.

Select the desktop rig's BetweenUsCompositePalette component or use the studio's Glass palette
dropdown to switch SilverNeutral, CalmBlue, WarmChampagne, ReflectiveLilac or TenseRose.
These are manual art-direction studies, not an automatic emotion classifier.
The silver preset is the saved default. PNG variants and 4K alpha exports are in
CompositeCaptures/SeatedPeople/.
