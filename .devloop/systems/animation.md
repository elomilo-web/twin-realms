# Animation
Purpose: Character rigs and animation: Quaternius universal-rig Avatar with clip playback, procedural HeroRig fallback, foot IK, kit props, wings.
## Files
- `scripts/Avatar.cs` (755 lines): Quaternius rig character from data/avatar.json, slots, hair, cape, wings, hand sockets. Key: `Build()` L37, `HoldXf()` L179, `HandPos()` L194, `ClipLength()` L202, `PoseAt()` L204, `SetHair()` L291, `AddCape()` L300, `AddWings()` L311, `FlapWings()` L369, `Socket()` L380, `HandSocket()` L382
- `scripts/HeroRig.cs` (280 lines): procedural primitive body, skin/race looks, gear. Key: `Build()` L39, `ApplyGear()` L101, `Pose()` L231, `SolveArm()` L253
- `scripts/FootIK.cs` (164 lines): foot planting on slopes via SkeletonModifier3D. Key: `_ProcessModificationWithDelta()` L25, `Rot()` L136, `Attach()` L145
- `scripts/Kit.cs` (155 lines): Quaternius env kit loader and merger. Key: `Get()` L26, `BoxMesh()` L64, `Add()` L122, `Models()` L139
- `scripts/Wings.cs` (38 lines): Golden Wings cape item. Key: `Make()` L24
## Scenes / nodes
Built in code: Player/Model (HeroRig), Player/Avatar/Body, Avatar "Anim" AnimationPlayer, BoneAttachment3D "At_<bone>", "Hold_<slot>", "Cape".
## Data
data/avatar.json, data/races.json, data/env.json.
## Links
[[player]] [[combat]] [[world]] [[blockworld]] [[loot]] [[skills_vfx]]
## Scores
- visuals 3/10: player_closeup and running show untextured flat-shaded grey clothing, plastic shield, waxy skin; zero PBR cloth, no face detail.
- feel 5/10: foot IK helps grounding, but clip blending and attack animation weight look stiff in combat_swing.
- code 5/10: Avatar.cs at 755 lines mixes build, tint, sockets and wings; HeroRig is a legacy parallel path.
## Ideas
- Gothic PBR materials on Avatar `Tint()` L108 (worn cloth normal maps, roughness 0.9, no emissive tint).
- Split wings/cape/hair out of `Avatar.cs` L291-L378 into AvatarExtras.cs.
- Animation blend tree with root-motion-ish attack lunges via `PoseAt()` L204 / AnimationTree.
- Cloth sim or secondary-motion bones for `AddCape()` L300.
- Add look-at head IK toward target/lock-on.
- Remove HeroRig fallback once Avatar covers all races.
- Weapon hold offsets tuned per clip with data in avatar.json read by `HoldXf()` L179.
