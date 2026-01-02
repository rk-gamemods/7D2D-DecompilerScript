# Key Game Methods Reference

This file documents important methods in 7 Days to Die that are commonly patched or referenced.

## Inventory Methods

Methods related to player/container inventory management:

| Type | Method | Callers | Callees |
|------|--------|---------|---------|
| ItemStack | IsEmpty | 280 | 0 |
| ItemStack | Clone | 271 | 4 |
| ItemStack | .ctor | 169 | 0 |
| ItemStack | Read | 39 | 2 |
| XUiM_PlayerInventory | AddItem | 37 | 1 |
| ItemStack | CreateArray | 35 | 1 |
| ItemStack | .ctor | 34 | 1 |
| XUiC_ItemStack | PlayPlaceSound | 31 | 6 |
| XUiC_ItemStack | HandleSlotChangeEvent | 29 | 3 |
| ItemStack | Write | 27 | 1 |
| Inventory | DecItem | 24 | 12 |
| ItemStack | Clear | 23 | 1 |
| Bag | AddItem | 17 | 4 |
| Inventory | AddItem | 17 | 1 |
| Bag | DecItem | 16 | 12 |
| Inventory | DecHoldingItem | 16 | 6 |
| XUiM_PlayerInventory | DropItem | 14 | 4 |
| ItemStack | CanStackWith | 13 | 3 |
| XUiC_ItemStackGrid | Init | 13 | 1 |
| vp_Inventory | HaveItem | 12 | 1 |
| XUiM_ItemStack | CheckKnown | 12 | 9 |
| Inventory | notifyListeners | 11 | 2 |
| ItemStack | CanStackPartly | 11 | 2 |
| Inventory | IsHoldingItemActionRunning | 10 | 1 |
| ItemStack | Equals | 10 | 1 |
| XUiC_ItemStackGrid | UpdateBackend | 10 | 0 |
| XUiC_ItemStackGrid | OnOpen | 10 | 4 |
| Inventory | CanTakeItem | 9 | 2 |
| Inventory | Execute | 9 | 3 |
| XUiC_ItemStackGrid | OnClose | 9 | 5 |


## UI Methods

Methods related to the XUi system:

| Type | Method | Callers | Callees |
|------|--------|---------|---------|
| XUiController | RefreshBindings | 378 | 0 |
| XUiController | Init | 306 | 4 |
| XUiController | Update | 231 | 5 |
| XUiController | OnOpen | 229 | 5 |
| XUiController | OnClose | 155 | 3 |
| XUiController | ParseAttribute | 100 | 0 |
| XUiC_GamepadCalloutWindow | AddCallout | 95 | 3 |
| XUi | FindWindowGroupByName | 77 | 0 |
| XUiController | SelectCursorElement | 66 | 18 |
| XUiC_Radial | CreateRadialEntry | 47 | 1 |
| XUiC_ItemActionList | AddActionListEntry | 42 | 1 |
| XUiM_PlayerInventory | AddItem | 37 | 1 |
| XUiC_GamepadCalloutWindow | ClearCallouts | 36 | 1 |
| XUiC_GamepadCalloutWindow | EnableCallouts | 36 | 1 |
| XUiC_GamepadCalloutWindow | DisableCallouts | 35 | 1 |
| XUi | IsGameRunning | 32 | 1 |
| XUiC_ItemStack | PlayPlaceSound | 31 | 6 |
| XUiC_ItemStack | HandleSlotChangeEvent | 29 | 3 |
| XUi | LoadData | 26 | 1 |
| XUiC_WindowSelector | OpenSelectorAndWindow | 26 | 6 |
| XUiController | OnHovered | 24 | 0 |
| XUiController | RegisterForInputStyleChanges | 21 | 2 |
| XUiController | Cleanup | 20 | 5 |
| XUiC_Paging | PageUp | 20 | 7 |
| XUiC_Paging | PageDown | 20 | 7 |
| XUiController | InputStyleChanged | 19 | 0 |
| XUi | PatchAndLoadXuiXml | 18 | 4 |
| XUiC_MapWaypointList | UpdateWaypointsList | 17 | 39 |
| XUiC_OptionsController | AddControllerLabelMappingsForButton | 16 | 5 |
| XUiC_PopupMenu | AddItem | 16 | 0 |


## Player Methods

Methods related to player entity:

| Type | Method | Callers | Callees |
|------|--------|---------|---------|
| EntityPlayer | PlayOneShot | 26 | 1 |
| EntityPlayer | IsInParty | 18 | 0 |
| EntityPlayerLocal | shouldPushOutOfBlock | 15 | 7 |
| EntityPlayerLocal | StartTPCameraLockTimer | 11 | 0 |
| EntityPlayerLocal | CrosshairAlpha | 8 | 0 |
| EntityPlayerLocal | AddUIHarvestingItem | 8 | 2 |
| EntityPlayerLocal | HolsterWeapon | 8 | 0 |
| EntityPlayer | AddKillXP | 7 | 9 |
| EntityPlayer | Teleport | 6 | 7 |
| EntityPlayer | IsPartyLead | 6 | 1 |
| EntityPlayerLocal | _GetNASlots | 6 | 0 |
| EntityPlayerLocal | shakeCamera | 6 | 0 |
| EntityPlayerLocal | NAEquip | 5 | 2 |
| EntityPlayerLocal | SwitchFirstPersonViewFromInput | 5 | 5 |
| EntityPlayerLocal | SwitchToPreferredCameraMode | 5 | 4 |
| EntityPlayerLocal | TeleportToPosition | 5 | 4 |
| EntityPlayer | TriggerSharedQuestRemovedEvent | 4 | 0 |
| EntityPlayer | DetectUsScale | 4 | 1 |
| EntityPlayer | LeaveParty | 4 | 1 |
| EntityPlayerLocal | checkedGetFPController | 4 | 4 |
| EntityPlayerLocal | IsCameraAttachedToPlayerOrScope | 4 | 0 |
| EntityPlayerLocal | pushOutOfBlocks | 4 | 15 |
| EntityPlayerLocal | CancelInventoryActions | 4 | 18 |
| EntityPlayerLocal | Respawn | 4 | 9 |
| EntityPlayerLocal | EnableCamera | 4 | 0 |
| EntityPlayerLocal | EnableAutoMove | 4 | 1 |
| EntityPlayer | TriggerQuestChangedEvent | 3 | 0 |
| EntityPlayer | TriggerQuestRemovedEvent | 3 | 0 |
| EntityPlayer | Respawn | 3 | 2 |
| EntityPlayer | HasTwitchMember | 3 | 1 |


## Most Called Methods

Top 50 most called methods in the game:

| Type | Method | Times Called |
|------|--------|--------------|
| XUiController | get_xui | 2394 |
| Localization | Get | 1719 |
| GameManager | get_World | 1556 |
| XUi | get_playerUI | 1329 |
| XUiController | GetChildById | 1221 |
| XUiController | get_ViewComponent | 1089 |
| XmlExtensions | GetAttribute | 946 |
| BlockValue | get_Block | 924 |
| SdtdConsole | Output | 762 |
| XmlExtensions | HasAttribute | 707 |
| LocalPlayerUI | get_windowManager | 630 |
| Vector3i | .ctor | 609 |
| XUiController | GetChildByType | 553 |
| BaseObjective | get_OwnerQuest | 541 |
| ConnectionManager | get_IsServer | 515 |
| EnumUtils | ToStringCached | 514 |
| Row | .ctor | 512 |
| LocalPlayerUI | get_entityPlayer | 497 |
| NetPackageManager | GetPackage | 487 |
| ItemValue | get_ItemClass | 483 |
| XUiView | set_IsVisible | 450 |
| PooledBinaryReader | ReadInt32 | 409 |
| EffectManager | GetValue | 386 |
| StringParsers | ParseFloat | 378 |
| XUiController | RefreshBindings | 378 |
| GamePrefs | GetInt | 375 |
| LocalPlayerUI | get_xui | 352 |
| DynamicProperties | ParseString | 341 |
| PlatformManager | get_NativePlatform | 341 |
| StringParsers | ParseBool | 333 |
| Block | get_Properties | 324 |
| World | toChunkXZ | 324 |
| PooledBinaryWriter | Write | 321 |
| ParserUtils | ParseStringAttribute | 319 |
| World | GetEntity | 313 |
| QuestEventManager | get_Current | 311 |
| Vector3i | ToVector3 | 306 |
| XUiController | Init | 306 |
| Extensions | EqualsCaseInsensitive | 293 |
| Vector2i | .ctor | 283 |
| TwitchManager | get_Current | 283 |
| ItemStack | IsEmpty | 280 |
| BaseAction | get_Owner | 274 |
| BlockValue | get_isair | 271 |
| ItemStack | Clone | 271 |
| World | GetPrimaryPlayer | 271 |
| PooledBinaryWriter | Write | 269 |
| PropertyDecl | .ctor | 262 |
| Row | .ctor | 256 |
| Utils | Fastfloor | 256 |
