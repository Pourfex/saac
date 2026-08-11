### PSI Events Overview

This document describes all Unity → Psi events produced by the `Experience` scene (including the `LogManager` object), with their **topic name**, **payload type**, and **field‑by‑field meaning**.

---

### 1. Simple scalar events

- **`Vitesse Tapis`**
  - **Type**: `float` (`System.Single`)
  - **Description**: Current treadmill speed.
  - **Fields**:
    - `value` (`float`): Treadmill speed (units defined by gameplay, typically m/s or normalized 0–1).

- **`Levier`**
  - **Type**: `float` (`System.Single`)
  - **Description**: Analog state of the main lever.
  - **Fields**:
    - `value` (`float`): Lever position (e.g. 0 = idle, 1 = fully activated, continuous in between).

- **`D1-Poubelle`**, **`D2-Poubelle`**
  - **Type**: `int` (`System.Int32`)
  - **Description**: State or code associated with the trash modules.
  - **Fields**:
    - `value` (`int`): Discrete code for the bin state (exact mapping defined in trash‑module logic).

---

### 2. Boolean flag events

Produced by `PsiExporterBoolean` (`bool` → `PsiFormatBoolean`).

- **Common payload**
  - **Type**: `bool` (`System.Boolean`)
  - **Field**:
    - `value` (`bool`): meaning depends on topic (see below).

- **Per‑topic meaning**
  - **`ERROR`**
    - `true`: the host has detected or raised an error condition in the scenario.
    - `false`: no error currently signalled.
  - **`M1-Validation`**, **`M2-Validation`**
    - `true`: validation button on generator **M1** / **M2** has been pressed (participant confirms their module configuration).
    - `false`: button released or idle.
  - **`M1-ModuleOut`**, **`M2-ModuleOut`**
    - Source: `TriggerEvent` with `exitModules` mode, wired via `moduleOutExporter`.
    - `true`: a module for side 1 / 2 has exited the generator area (conveyor end).
    - `false`: optional reset back to “no module out” (not always emitted).
  - **`M1-ModuleOutZone`**, **`M2-ModuleOutZone`**
    - `true`: module for side 1 / 2 has left its designated **zone** (safety / tracking area).
    - `false`: module is back inside the zone.
  - **`Bouton urgence`**
    - Source: `EmergencyButtonPsi`.
    - `true`: emergency button pressed (experiment should be considered stopped/aborted).
    - `false`: emergency button released (back to normal).

---

### 3. String message events

Produced by `PsiExporterString` (`string` → `PsiFormatString`).

- **Topics**:
  - `M1-SelectModule`
  - `M2-SelectModule`
  - `Ping`
  - `BatterySpawn`
  - `BatteryRegulatedTime`
  - `BatteryFire`
  - `RemoveModule`
  - `AddModule`

- **Type**: `string` (`System.String`)
- **Description**: Human‑readable labels for discrete actions and system messages.
- **Fields**:
  - `message` (`string`): Free‑form text describing the event (selected module, ping payload, battery event details, module name, etc.).

---

### 4. `(int, string)` – module status

- **Topic**: `Module status`
- **Type**: `System.ValueTuple<int, string>`
- **Exporter**: `psiExporterIntString` (`PsiFormatIntString`)
- **Description**: Per‑module status report.
- **Fields**:
  - `Item1` (`int id`): Numeric module identifier.
  - `Item2` (`string status`): Human‑readable status text (e.g. `OK`, `Error`, `Missing`).

---

### 5. `(int, bool)` – ID + binary state

- **Topics**: `E1-extincteur`, `E2-extincteur`
- **Type**: `System.ValueTuple<int, bool>`
- **Exporter**: `PsiExporterIntBool` (`PsiFormatIntBool`)
- **Description**: Participant action on each fire extinguisher.
- **Fields** (see `Documentation/StageDorian/Evenements/Events.MD`):
  - `Item1` (`int clientId`): ID of the participant (network client) using the extinguisher.
  - `Item2` (`bool activated`):
    - `true`: extinguisher handle squeezed (extinction action active).
    - `false`: handle released (extinction action stopped).

---

### 6. `(int, bool, string)` – ID + state + label

Produced by `PsiExporterIntBoolString` and `PsiExporterGrab` (`(int,bool,string)` → `PsiFormatIntBoolString`).

- **Common payload**
  - **Type**: `System.ValueTuple<int, bool, string>`
  - **Field names**: `(id, state, info)`.

- **Per‑topic meaning**

  - **`Area1`**, **`Area2`** (zone entry/exit – see `TriggerEvent`)
    - `id` (`int`):
      - `-1`: a **player** entered/left the zone without a specific module.
      - `>= 0`: ID of the **module** entering/leaving the zone (from `Module.id.Value`).
    - `state` (`bool`):
      - `true`: entity **entered** the zone.
      - `false`: entity **exited** the zone.
    - `info` (`string`): name of the logical zone, exactly `myEvent.ToString()` from `TriggerEvent`, e.g. `"GeneratorArea"`, `"TVArea"`, `"LevierArea"`, `"CarpetArea"`.

  - **`Gaze`** (coarse object/avatar gaze, via `EyeInteractable`)
    - `id` (`int`): network client ID of the gazing participant (`NetworkManager.Singleton.LocalClientId`).
    - `state` (`bool`):
      - `true`: participant is currently **gazing at** the target object or avatar.
      - `false`: gaze has **left** this target.
    - `info` (`string`):
      - For objects: the **GameObject name**.
      - For avatars: the avatar identifier converted to string (`avatarID.ToString()`).

  - **`Grab1`**, **`Grab2`** (object grabs, via `NetworkGrabable`)
    - `id` (`int`):
      - For vehicles/bornes (`tag` is `"Voiture"`, `"moto"`, `"Born"`): `Module.id.Value` of the grabbed object.
      - Otherwise: `0` (generic grab with no module ID).
    - `state` (`bool`):
      - `true`: object has just been **grabbed**.
      - `false`: object has just been **released**.
    - `info` (`string`): the Unity **tag** of the object (e.g. `"Voiture"`, `"moto"`, `"Born"`, or another tag).

  - **`Collision sol`**, **`Collision tapis1`**, **`Collision tapis2`**, **`Collision batterie`** (via `CollisionLogger`)
    - `id` (`int`):
      - If the colliding object has an `ID` component: that `ID.ID` value.
      - Otherwise: a fallback numeric identifier (`GameObject.GetInstanceID()`).
    - `state` (`bool`):
      - `true`: **enter / start** of contact (`OnCollisionEnter` / `OnTriggerEnter`).
      - `false`: **exit / end** of contact (`OnCollisionExit` / `OnTriggerExit`).
    - `info` (`string`): structured descriptor  
      `"{collided}:{collidingName}:{idPart}:{batteryPart}"`, where:
      - `collided`: name of the object that owns the `CollisionLogger`.
      - `collidingName`: name of the other object.
      - `idPart`: ID of the colliding object, or `"null"` if absent.
      - `batteryPart`: `"B_<batteryId>"` if the logger is under a `Batteries` object, otherwise `"null"`.

---

### 7. `PsiBatterie` – battery state

- **Topic**: `Batteries`
- **Type**: `PsiBatterie` (`PsiFormatBat`)
- **Description**: Snapshot of a battery’s configuration and state.
- **Fields** (class defined in `PsiBatterie.cs`):
  - `id` (`int`): Unique battery identifier.
  - `tension` (`int`): Battery voltage level (or encoded level).
  - `places` (`int`): Number of available slots/cells.
  - `regulated` (`bool`): Whether the battery is in a regulated (valid) state.
  - `modules` (`int[]`): IDs of modules associated with or inserted into this battery.
  - `state` (`string`): High‑level battery state label (`Idle`, `Charging`, `Fault`, etc.).
  - `dist` (`float`): Distance or other continuous metric (e.g. distance from target location).

---

### 8. Door opening – `(bool, Vector3)`

- **Topics**: `Porte1 ouverture`, `Porte2 ouverture`
- **Type**: `System.ValueTuple<bool, System.Numerics.Vector3>`
- **Exporter**: `PsiExporterDoor` (`PsiFormatBoolVector3`)
- **Description**: Door open/closed state with current orientation.
- **Fields**:
  - `Item1` (`bool open`):
    - `true`: door considered open (local Y angle in the 90–95° range).
    - `false`: door considered closed.
  - `Item2` (`Vector3 orientation`): Door local Euler angles in degrees `(x, y, z)`.

---

### 9. Pose & gaze/eye tracking – `Tuple<Vector3, Vector3>`

Produced by `PsiExporterPositionOrientation` / `PsiExporterPositionOrientationRaw` (`Tuple<Vector3,Vector3>` → `PsiFormatPositionOrientation`).

- **Participant 1 topics**:
  - `1-Head`, `1-LeftWrist`, `1-RightWrist`
  - `1-GazeHeadOrientation`, `1-EyeLeft`, `1-EyeRight`

- **Participant 2 topics**:
  - `2-Head`, `2-LeftWrist`
  - `2-GazeHeadOrientation`, `2-EyeLeft`, `2-EyeRight`

- **Type**: `Tuple<System.Numerics.Vector3, System.Numerics.Vector3>`
- **Description**: 3D position plus 3D orientation of body and eye anchors.
- **Fields**:
  - `Item1` (`Vector3 position`): World‑space position `(x, y, z)` in Unity units (meters).
  - `Item2` (`Vector3 orientation`): World‑space Euler rotation `(pitch, yaw, roll)` in degrees.

Stream grouping on the Psi side:
- Topics starting with `1-` → store in `PositionRotation_1`.
- Topics starting with `2-` → store in `PositionRotation_2`.

---

### 10. TV control – `(int, int, int, string)`

- **Topic**: `TV`
- **Type**: `System.ValueTuple<int, int, int, string>`
- **Exporter**: `PsiExporterTV` (`PsiFormatTV`)
- **Description**: Control commands and state for the TV UI.
- **Fields**:
  - `Item1` (`int clientId`): ID of the client who pressed the TV command button.
  - `Item2` (`int batteryId`): `Batteries.id.Value` for the battery currently displayed on the TV.
  - `Item3` (`int sideCode`): `0` for Left, `1` for Right button.
  - `Item4` (`string sideLabel`): `"Left"` or `"Right"`.

---

### 11. Rich gaze events – `ObjectGazeEvent`

Used by `PsiExporterGazeEvent` / `PsiGazeEventExporter` and serialized via `PsiFormatGazeObjectEvent`.

- **Type**: `ObjectGazeEvent`
- **Wire layout** (write order in `PsiFormatGazeObjectEvent`):
  - `userID` (`int`): ID of the gazing participant/avatar.
  - `objectID` (`string`): Identifier of the object being gazed at.
  - `type` (`string`): Category of gaze event (e.g. `"object"`, `"area"`).
  - `status` (`bool`): `true` when gaze is active, `false` when it ends.

This provides a higher‑level, object‑centric gaze stream, complementary to the continuous eye and head pose streams.

---

### 12. Battery completion – `PsiBatteryFinish`

- **Topic**: `BatteryFinish`
- **Type**: `PsiBatteryFinish` (`PsiFormatBatteryFinish`)
- **Description**: Detailed summary of the participant’s final battery configuration.
- **Fields** (class defined in `PsiFormatBatteryFinish.cs`):
  - `negativeBorn` (`bool`): Negative terminal correctly configured.
  - `positiveBorn` (`bool`): Positive terminal correctly configured.
  - `frontBorn` (`bool`): Front terminal correctly connected.
  - `backBorn` (`bool`): Back terminal correctly connected.
  - `onlyTwoBorns` (`bool`): Scenario expects only two terminals.
  - `completedSpaces` (`int`): Number of correctly filled slots/cells.
  - `totalSpaces` (`int`): Total number of slots/cells.
  - `givenVoltage` (`int[]`): Voltages actually provided in each slot.
  - `voltagesRequired` (`int[]`): Target voltages required per slot.
  - `matchVoltages` (`int`): Count or score of correctly matched voltages.
  - `regulated` (`bool`): Final configuration is in a regulated/valid state.

---

### 13. JSON configuration hint

Each topic above can be represented in a Psi configuration JSON with:
- **`topic`**: the exact topic name,
- **`type`**: the C# payload type (`System.Single`, `System.Int32`, tuples, or custom classes like `PsiBatterie` / `PsiBatteryFinish`),
- **`classFormat`**: the Psi format class (`PsiFormatFloat`, `PsiFormatIntBoolString`, `PsiFormatBat`, `PsiFormatBatteryFinish`, etc.),
- **`streamToStore`**: logical store name (`Events`, `PositionRotation_1`, `PositionRotation_2`).
