# The weapon's shock wave - analysis and plan (fidelity finding F-63)

Read on 28 October 2026 for Reforged's R-24 (`docs/REFORGED-FIXES.md`). Nothing of it is built; this is the starting point of the work. Sources: EA's released code
(`references/ea-generals-zero-hour`), read, never copied.

## 1. What the original does

**Where it starts.** `Weapon::dealDamageInternal` (`Weapon.cpp` ~1409-1455), for every victim of an area weapon with `ShockWaveAmount > 0`: the shock wave vector is
the victim's position minus the source object's position (straight up when they coincide), with the weapon's `ShockWaveRadius` and `ShockWaveTaperOff`, written into the
`DamageInfo`. Eleven weapons of the shipped data carry it (the nukes, the MOAB, the daisy cutter, the fuel-air bomb, the Aurora's bomb, the sneak attack's and the bunker
buster's shock waves, the demo trap, a Troop Crawler dummy weapon).

**What it does to the victim.** `Object::attemptDamage` (`Object.cpp` ~1818-1860), after the body took the damage, for a victim with a `PhysicsBehavior` that is no
airborne target and no projectile:
- taper: `distanceFromCenter = min(1, |vector| / radius)`, `mult = 1 - distanceFromCenter * (1 - taperOff)`;
- force: the vector normalised, scaled by `amount * mult`, and its `z` set to the length of that force ("up force equal to the lateral force for dramatic effect");
- `PhysicsBehavior::applyShock`: the force scaled by `1 - clamp(ShockResistance, 0, 1)`, then `applyForce` (acceleration += force / mass; a motive object keeps only
  the lateral part);
- `applyRandomRotation`: unless `STICK_TO_GROUND`, bouncing is allowed and yaw, pitch and roll rates each gain `ShockMax* x GameLogicRandomValue(-1, 1)` - three draws of
  the logic stream, in that order;
- `setStunned(true)` and the model condition `STUNNED_FLAILING`.

**The flight** (`PhysicsBehavior::update`, `PhysicsUpdate.cpp` 627-925, every logic frame while there is velocity): gravity and friction (on or near the ground:
yaw/pitch/roll damped by 0.85 and lateral and forward friction relative to the object's facing, `ForwardFriction`, `LateralFriction`, at least 0.01, at most 0.99, times the
mass; in the air: `AerodynamicFriction` against the velocity), velocity += acceleration (components under 0.001 become 0), position += velocity (only `z` while
`OBJECT_STATUS_BRAKING`); the rotation by the three rates times `PitchRollYawFactor` (pitch bent by `CenterOfMassOffset`); the ground clamp with a bounce
(`handleBounce`: with bouncing allowed and the object at or below the ground, an upward force of `|vz| x GroundStiffness` clamped to 0.01-0.99, the rates damped by 0.7,
an object upside down turned over; a real bounce runs `testStunnedUnitForDestruction`), the object righted after a bounce, the falling damage on landing (`DAMAGE_FALLING`,
`DEATH_SPLATTED`, when the downward speed exceeds `MinFallHeightForDamage`'s speed and the fall was steep - more than about 71 degrees - by
`netSpeed x mass x FallHeightDamageFactor`), `KillWhenRestingOnGround`.

**The stun.** `Locomotor::locoUpdate_moveTowardsPosition` and the other move (`Locomotor.cpp` 886, 979) do nothing while the physics module is stunned. The stun ends
(`PhysicsUpdate.cpp` 695-708) when the velocity is below 0.5 in all three axes **or** the object is not significantly above the terrain - tested on the position of the frame
before (the matrix is applied only at the end of the update). Consequences in the original:
- a unit shocked where it stands loses the stun in the very next update, before it has left the ground; the locomotor then drives while the unit flies;
- the clear removes `STUNNED`, not `STUNNED_FLAILING`: the flailing pose of such a unit outlasts its stun (the first half of R-24);
- the landing switch from `STUNNED_FLAILING` to `STUNNED` and `testStunnedUnitForDestruction` (killed when it lands upside down, off the map, on a cliff or under water
  without a locomotor for it) need `IS_STUNNED` at the landing - rare, because the stun has mostly ended by then (Reforged measured no stunned landing in a carpet bomb).

Reforged changed the stun's end to "at rest *and* on the ground" and reads `ExtraBounciness` in `handleBounce` (its `3af3c9c6`); both are changes of the original's
behaviour (**D** and **B** in `docs/REFORGED-FIXES.md`), not this finding.

## 2. What the port has

`PhysicsBehavior` (`PhysicsAndMinefieldModules.cs`) models one force - gravity on an object let go in the air (a bomb) - and rests otherwise; ground units move by the
movement system, not by forces. `ShockWaveAmount`, `ShockWaveRadius`, `ShockWaveTaperOff` are deferred fields (compatibility baseline), and of `PhysicsBehavior` only
`Mass`, `AllowBouncing`, `AllowCollideForce`, `KillWhenRestingOnGround` are read. The model condition `STUNNED_FLAILING` and `STUNNED` are never set.

## 3. Proposed plan (one cause per step)

| Step | What | Manifest | Test |
| --- | --- | --- | --- |
| 1 | Type the three weapon fields and the physics fields the flight reads (`ShockResistance`, `ShockMax*`, the frictions, `CenterOfMassOffset`, `PitchRollYawFactor`, `MinFallHeightForDamage`, `FallHeightDamageFactor`); the shock wave vector in the damage of an area weapon | input manifest | the fields typed; compatibility baselines lose their entries |
| 2 | The flight of a shocked object: velocity per original frame, gravity, ground and air friction relative to the facing, the ground clamp and bounce, pitch/roll/yaw as state of the simulation (the snapshot carries them), falling damage on landing; the movement system adds its own step as the locomotor's motive force does | reference manifest (every run with one of the eleven weapons) | a tank 50 from a shock wave flies away from it and lands where the 30 Hz step lands at 60 and 120 Hz; a soldier falls hard enough to take damage; an object with `ShockResistance` 1 does not move |
| 3 | The stun as the original has it: the move of a stunned unit does nothing; the stun ends on the previous frame's "at rest or not above the terrain"; `STUNNED_FLAILING` set and not cleared by that end; the landing switch and `testStunnedUnitForDestruction` when the unit is still stunned | reference manifest | a unit shocked where it stands is stunned for one frame and keeps flailing; one shocked in the air while stunned is killed landing upside down |
| 4 | Presentation: pitch and roll drawn, `STUNNED_FLAILING` and `STUNNED` animations | picture pins of scenes with a superweapon | - |

Rate invariance: the flight is a 30 Hz step (decided in the first tick of the object's frame, read at the frame end, `docs/SIMULATION-CORE.md` §43); the three random draws
happen in the tick of the damage, in victim order, as the original's.

## 3a. Read for step 2 (28 October 2026)

- **Infantry does not fly.** `Locomotor::locoUpdate` hands the locomotor's `StickToGround` (typed, `UpgradeSchema.cs`; infantry locomotors set it) to the physics
  module (`setStickToGround`); such an object skips `applyRandomRotation` and is put back on the ground every update unless `ALLOW_TO_FALL` (`PhysicsUpdate.cpp` 786-789):
  a shocked soldier slides along the ground, it does not rise or tumble.
- **The force is upward by the full amount.** After `normalize` and `scale(amount x mult)` the force has the length `amount x mult`, so `z = length` sets the upward part to
  exactly that, whatever the vector's own `z` was.
- **The port's movement pins a ground unit to the terrain** every step it moves it (`Phase5Modules.cs`, the ground clamp of the move), so a flight needs the movement
  paused while the object is in the air - a named difference: the original's locomotor would push again as soon as the (quirky) stun has ended.
- **Where it hooks in:** `SimulationWorld.DealDamageCore`, after each victim's `Damage`, in victim order (the three random draws there); the vector from the source's
  position (the frame end above 30 Hz, as the victim's) to the victim's; a source that is gone gives the original's zero vector, i.e. straight up.
- `GroundStiffness` is 0.8 in the shipped `GameData.ini`.
- **What step 2 must read first** (none is read by the port today): the locomotor's `StickToGround` (`LocomotorInfo`), `GameData`'s `GroundStiffness`, and the original's
  `Object::isAirborneTarget` (an aircraft in the air is not shocked) as a query of the world.

## 4. Not determined

- The position the original takes as the source of the vector for a projectile's detonation (`source` of `dealDamageInternal` - the projectile or its launcher).
- How much of the movement system's step stands in for the locomotor's motive force during a flight without changing ordinary movement.
