# Game Events Reference

This file documents the event system in 7 Days to Die - events that can be subscribed to for modding.

## Event Declarations

Events defined in game code that mods can subscribe to:

| Owner Type | Event Name | Delegate Type |
|------------|------------|---------------|
| Bag | OnBackpackItemsChangedInternal | XUiEvent_BackpackItemsChangedInternal |
| BaseObjective | ValueChanged | ObjectiveValueChanged |
| Challenges.BaseChallengeObjective | ValueChanged | ObjectiveValueChanged |
| Challenges.Challenge | OnChallengeStateChanged | ChallengeStateChanged |
| ChunkCluster | OnBlockChangedDelegates | OnBlockChangedDelegate |
| ChunkCluster | OnBlockDamagedDelegates | OnBlockDamagedDelegate |
| ChunkCluster | OnChunkVisibleDelegates | OnChunkVisibleDelegate |
| ChunkCluster | OnChunksFinishedDisplayingDelegates | OnChunksFinishedDisplayingDelegate |
| ChunkCluster | OnChunksFinishedLoadingDelegates | OnChunksFinishedLoadingDelegate |
| ConnectionManager | OnClientAdded | ClientConnectionAction |
| ConnectionManager | OnClientDisconnected | ClientConnectionAction |
| ConnectionManager | OnDisconnectFromServer | Action |
| CraftingManager | RecipeUnlocked | OnRecipeUnlocked |
| DataItem | OnChangeDelegates | OnChangeDelegate |
| DiscordManager | ActivityInviteReceived | ActivityInviteReceivedCallback |
| DiscordManager | ActivityJoining | ActivityJoiningCallback |
| DiscordManager | AudioDevicesChanged | AudioDevicesChangedCallback |
| DiscordManager | CallChanged | CallChangedCallback |
| DiscordManager | CallMembersChanged | CallMembersChangedCallback |
| DiscordManager | CallStatusChanged | CallStatusChangedCallback |
| DiscordManager | FriendsListChanged | FriendsListChangedCallback |
| DiscordManager | LobbyMembersChanged | LobbyMembersChangedCallback |
| DiscordManager | LobbyStateChanged | LobbyStateChangedCallback |
| DiscordManager | LocalUserChanged | LocalUserChangedCallback |
| DiscordManager | PendingActionsUpdate | PendingActionsUpdateCallback |
| DiscordManager | RelationshipChanged | RelationshipChangedCallback |
| DiscordManager | SelfMuteStateChanged | SelfMuteStateChangedCallback |
| DiscordManager | StatusChanged | Action<EDiscordStatus> |
| DiscordManager | UserAuthorizationResult | UserAuthorizationResultCallback |
| DiscordManager | VoiceStateChanged | VoiceStateChangedCallback |
| DiscordSettings | AutoJoinVoiceModeChanged | Action<EAutoJoinVoiceMode> |
| DiscordSettings | DmPrivacyModeChanged | Action<bool> |
| DiscordSettings | InputDeviceChanged | Action<string> |
| DiscordSettings | InputVolumeChanged | Action<int> |
| DiscordSettings | OutputDeviceChanged | Action<string> |
| DiscordSettings | OutputVolumeChanged | Action<int> |
| DiscordSettings | VoiceModePttChanged | Action<bool> |
| DiscordSettings | VoiceVadModeChanged | Action<bool> |
| DiscordSettings | VoiceVadThresholdChanged | Action<int> |
| DynamicMusic.Legacy.ObjectModel.InstrumentID | OnLoadFinished | LoadFinishedAction |
| DynamicPrefabDecorator | OnPrefabChanged | Action<PrefabInstance> |
| DynamicPrefabDecorator | OnPrefabLoaded | Action<PrefabInstance> |
| DynamicPrefabDecorator | OnPrefabRemoved | Action<PrefabInstance> |
| DynamicUIAtlas | AtlasUpdatedEv | Action |
| EntityPlayer | InvitedToParty | OnPartyChanged |
| EntityPlayer | PartyChanged | OnPartyChanged |
| EntityPlayer | PartyJoined | OnPartyChanged |
| EntityPlayer | PartyLeave | OnPartyChanged |
| EntityPlayer | PlayerTeleportedDelegates | OnPlayerTeleportDelegate |
| EntityPlayer | QuestAccepted | QuestJournal_QuestEvent |
| EntityPlayer | QuestChanged | QuestJournal_QuestEvent |
| EntityPlayer | QuestRemoved | QuestJournal_QuestEvent |
| EntityPlayer | SharedQuestAdded | QuestJournal_QuestSharedEvent |
| EntityPlayer | SharedQuestRemoved | QuestJournal_QuestSharedEvent |
| EntityPlayerLocal | DragAndDropItemChanged | Action |
| EntityPlayerLocal | InventoryChangedEvent | Action |
| Entry | ItemClicked | MenuItemClickedDelegate |
| Entry | ValueChanged | MenuItemValueChangedDelegate |
| Equipment | CosmeticUnlocked | Equipment_CosmeticUnlocked |
| Equipment | OnChanged | Action |
| EventSubClient | OnEventReceived | Action<JObject> |
| GameEventManager | GameBlockRemoved | OnGameBlockRemoved |
| GameEventManager | GameBlocksAdded | OnGameBlocksAdded |
| GameEventManager | GameBlocksRemoved | OnGameBlocksRemoved |
| GameEventManager | GameEntityDespawned | OnGameEntityChanged |
| GameEventManager | GameEntityKilled | OnGameEntityChanged |
| GameEventManager | GameEntitySpawned | OnGameEntityAdded |
| GameEventManager | GameEventAccessApproved | OnGameEventAccessApproved |
| GameEventManager | GameEventApproved | OnGameEventStatus |
| GameEventManager | GameEventCompleted | OnGameEventStatus |
| GameEventManager | GameEventDenied | OnGameEventStatus |
| GameEventManager | TwitchPartyGameEventApproved | OnGameEventStatus |
| GameEventManager | TwitchRefundNeeded | OnGameEventStatus |
| GameManager | OnClientSpawned | Action<ClientInfo> |
| GameManager | OnLocalPlayerChanged | OnLocalPlayerChangedEvent |
| GameManager | OnWorldChanged | OnWorldChangedEvent |
| GameOptionsManager | OnGameOptionsApplied | Action |
| GameOptionsManager | ResolutionChanged | Action<int, int> |
| GameOptionsManager | ShadowDistanceChanged | Action<int> |
| GameOptionsManager | TextureFilterChanged | Action<int> |
| GameOptionsManager | TextureQualityChanged | Action<int> |
| GamePrefs | OnGamePrefChanged | Action<EnumGamePrefs> |
| GameServerInfo | OnChangedAny | Action<GameServerInfo> |
| GameServerInfo | OnChangedBool | Action<GameServerInfo, GameInfoBool> |
| GameServerInfo | OnChangedInt | Action<GameServerInfo, GameInfoInt> |
| GameServerInfo | OnChangedString | Action<GameServerInfo, GameInfoString> |
| GameStats | OnChangedDelegates | OnChangedDelegate |
| ISaveDataManager | CommitFinished | Action |
| ISaveDataManager | CommitStarted | Action |
| ITileEntity | Destroyed | XUiEvent_TileEntityDestroyed |
| Inventory | OnToolbeltItemsChangedInternal | XUiEvent_ToolbeltItemsChangedInternal |
| LocalPlayerCamera | PreCull | Action<LocalPlayerCamera> |
| LocalPlayerCamera | PreRender | Action<LocalPlayerCamera> |
| LocalPlayerManager | OnLocalPlayersChanged | Action |
| LocalPlayerUI | OnEntityPlayerLocalAssigned | Action<EntityPlayerLocal> |
| LocalPlayerUI | OnUIShutdown | Action |
| Localization | LanguageSelected | Action<string> |
| MapObjectManager | ChangedDelegates | MapObjectListChangedDelegate |
| MapVisitor | OnVisitChunk | VisitChunkDelegate |
| MapVisitor | OnVisitMapDone | VisitMapDoneDelegate |
| MicroSplatTerrain | OnMaterialSync | MaterialSync |
| MicroSplatTerrain | OnMaterialSyncAll | MaterialSyncAll |
| NavObjectManager | OnNavObjectAdded | NavObjectChangedDelegate |
| NavObjectManager | OnNavObjectRefreshed | NavObjectChangedDelegate |
| NavObjectManager | OnNavObjectRemoved | NavObjectChangedDelegate |
| NewsManager | Updated | Action<NewsManager> |
| ObservableDictionary | EntryAdded | DictionaryAddEventHandler<TKey, TValue> |
| ObservableDictionary | EntryModified | DictionaryEntryModifiedEventHandler<TKey, TValue> |
| ObservableDictionary | EntryRemoved | DictionaryRemoveEventHandler<TKey, TValue> |
| ObservableDictionary | EntryUpdatedValue | DictionaryUpdatedValueEventHandler<TKey, TValue> |
| Party | PartyLeaderChanged | OnPartyChanged |
| Party | PartyMemberAdded | OnPartyMembersChanged |
| Party | PartyMemberRemoved | OnPartyMembersChanged |
| Platform.EOS.AntiCheatClientP2P | OnRemoteAuthComplete | Action |
| Platform.EOS.Api | ClientApiInitialized | Action |
| Platform.EOS.RequestDetails | Callback | IRemoteFileStorage.FileDownloadCompleteCallback |
| Platform.EOS.User | UserBlocksChanged | UserBlocksChangedCallback |
| Platform.EOS.User | UserLoggedIn | Action<IPlatform> |
| Platform.EOS.Voice | Initialized | Action |
| Platform.EOS.Voice | OnLocalPlayerStateChanged | Action<IPartyVoice.EVoiceChannelAction> |
| Platform.EOS.Voice | OnRemotePlayerStateChanged | Action<PlatformUserIdentifierAbs, IPartyVoice.EVoiceChannelAction> |
| Platform.EOS.Voice | OnRemotePlayerVoiceStateChanged | Action<PlatformUserIdentifierAbs, IPartyVoice.EVoiceMemberState> |
| Platform.IApplicationStateController | OnApplicationStateChanged | ApplicationStateChanged |
| Platform.IApplicationStateController | OnNetworkStateChanged | NetworkStateChanged |
| Platform.IPartyVoice | Initialized | Action |
| Platform.IPartyVoice | OnLocalPlayerStateChanged | Action<EVoiceChannelAction> |
| Platform.IPartyVoice | OnRemotePlayerStateChanged | Action<PlatformUserIdentifierAbs, EVoiceChannelAction> |
| Platform.IPartyVoice | OnRemotePlayerVoiceStateChanged | Action<PlatformUserIdentifierAbs, EVoiceMemberState> |
| Platform.IPlatformApi | ClientApiInitialized | Action |
| Platform.IPlatformMemoryStat | ColumnSetAfter | PlatformMemoryColumnChangedHandler<T> |
| Platform.IUserClient | UserBlocksChanged | UserBlocksChangedCallback |
| Platform.IUserClient | UserLoggedIn | Action<IPlatform> |
| Platform.Local.Api | ClientApiInitialized | Action |
| Platform.Local.User | UserBlocksChanged | UserBlocksChangedCallback |
| Platform.Local.User | UserLoggedIn | Action<IPlatform> |
| Platform.MultiPlatform.User | UserBlocksChanged | UserBlocksChangedCallback |
| Platform.MultiPlatform.User | UserLoggedIn | Action<IPlatform> |
| Platform.PlatformMemoryStat | ColumnSetAfter | PlatformMemoryColumnChangedHandler<T> |
| Platform.PlatformUserManager | BlockedStateChanged | PlatformUserBlockedStateChangedHandler |
| Platform.PlatformUserManager | DetailsUpdated | PlatformUserDetailsUpdatedHandler |
| Platform.PlayerInputManager | OnLastInputStyleChanged | Action<InputStyle> |
| Platform.Steam.Api | ClientApiInitialized | Action |
| Platform.Steam.SteamQueryPortReader | GameServerDetailsEvent | GameServerDetailsCallback |
| Platform.Steam.User | UserBlocksChanged | UserBlocksChangedCallback |
| Platform.Steam.User | UserLoggedIn | Action<IPlatform> |
| Platform.XBL.Api | ClientApiInitialized | Action |
| Platform.XBL.User | UserBlocksChanged | UserBlocksChangedCallback |
| Platform.XBL.User | UserLoggedIn | Action<IPlatform> |
| Platform.XBL.XblHelpers | OnError | ErrorDelegate |
| Platform.XBL.XblXuidMapper | UserIdentifierMapped | UserIdentifierMappedHandler |
| Platform.XBL.XblXuidMapper | XuidMapped | XuidMappedHandler |
| PlayerInteractions | OnNewPlayerInteraction | PlayerIteractionEvent |
| PrefabEditModeManager | OnPrefabChanged | Action<PrefabInstance> |
| QuestEventManager | AddItem | QuestEvent_ItemStackActionEvent |
| QuestEventManager | AssembleItem | QuestEvent_ItemStackActionEvent |
| QuestEventManager | BiomeEnter | QuestEvent_BiomeEvent |
| QuestEventManager | BlockActivate | QuestEvent_BlockEvent |
| QuestEventManager | BlockChange | QuestEvent_BlockChangedEvent |
| QuestEventManager | BlockDestroy | QuestEvent_BlockDestroyEvent |
| QuestEventManager | BlockPickup | QuestEvent_BlockEvent |
| QuestEventManager | BlockPlace | QuestEvent_BlockEvent |
| QuestEventManager | BlockUpgrade | QuestEvent_BlockEvent |
| QuestEventManager | BloodMoonSurvive | QuestEvent_Event |
| QuestEventManager | BuyItems | QuestEvent_PurchaseEvent |
| QuestEventManager | ChallengeAwardCredit | QuestEvent_ChallengeAwardCredit |
| QuestEventManager | ChallengeComplete | QuestEvent_ChallengeCompleteEvent |
| QuestEventManager | ContainerClosed | QuestEvent_OpenContainer |
| QuestEventManager | ContainerOpened | QuestEvent_OpenContainer |
| QuestEventManager | CraftItem | QuestEvent_ItemStackActionEvent |
| QuestEventManager | EntityKill | QuestEvent_EntityKillEvent |
| QuestEventManager | ExchangeFromItem | QuestEvent_ItemStackActionEvent |
| QuestEventManager | ExplosionDetected | QuestEvent_Explosion |
| QuestEventManager | HarvestItem | QuestEvent_HarvestStackActionEvent |
| QuestEventManager | HoldItem | QuestEvent_ItemValueActionEvent |
| QuestEventManager | NPCInteract | QuestEvent_NPCInteracted |
| QuestEventManager | NPCMeet | QuestEvent_NPCInteracted |
| QuestEventManager | QuestAwardCredit | QuestEvent_ChallengeAwardCredit |
| QuestEventManager | QuestComplete | QuestEvent_QuestCompleteEvent |
| QuestEventManager | RepairItem | QuestEvent_ItemValueActionEvent |
| QuestEventManager | ScrapItem | QuestEvent_ItemStackActionEvent |
| QuestEventManager | SellItems | QuestEvent_PurchaseEvent |
| QuestEventManager | SkillPointSpent | QuestEvent_SkillPointSpent |
| QuestEventManager | SleeperVolumePositionAdd | QuestEvent_SleeperVolumePositionChanged |
| QuestEventManager | SleeperVolumePositionRemove | QuestEvent_SleeperVolumePositionChanged |
| QuestEventManager | SleepersCleared | QuestEvent_SleepersCleared |
| QuestEventManager | TimeSurvive | QuestEvent_FloatEvent |
| QuestEventManager | TwitchEventReceive | QuestEvent_TwitchEvent |
| QuestEventManager | UseItem | QuestEvent_ItemValueActionEvent |
| QuestEventManager | WearItem | QuestEvent_ItemValueActionEvent |
| QuestEventManager | WindowChanged | QuestEvent_WindowChanged |
| SaveDataManager_Placeholder | CommitFinished | Action |
| SaveDataManager_Placeholder | CommitStarted | Action |
| TEFeatureAbs | Destroyed | XUiEvent_TileEntityDestroyed |
| ThreadManager | LateUpdateEv | Action |
| ThreadManager | UpdateEv | Action |
| TileEntity | Destroyed | XUiEvent_TileEntityDestroyed |
| TileEntityWorkstation | FuelChanged | XUiEvent_FuelStackChanged |
| TileEntityWorkstation | InputChanged | XUiEvent_InputStackChanged |
| TimerEventData | AlternateEvent | TimerEventHandler |
| TimerEventData | CloseEvent | TimerEventHandler |
| TimerEventData | Event | TimerEventHandler |
| TitleStorageOverridesManager | fetchFinished | Action<TSOverrides> |
| ToolTipEvent | EventHandler | ToolTipEventHandler |
| Twitch.PubSub.TwitchPubSub | OnBitsRedeemed | EventHandler<PubSubBitRedemptionMessage.BitRedemptionData> |
| Twitch.PubSub.TwitchPubSub | OnChannelPointsRedeemed | EventHandler<PubSubChannelPointMessage.ChannelRedemptionData> |
| Twitch.PubSub.TwitchPubSub | OnGoalAchieved | EventHandler<PubSubGoalMessage.Goal> |
| Twitch.PubSub.TwitchPubSub | OnSubscriptionRedeemed | EventHandler<PubSubSubscriptionRedemptionMessage> |
| Twitch.TwitchAuthentication | QRCodeGenerated | TwitchAuth_QRCodeGenerated |
| Twitch.TwitchLeaderboardStats | LeaderboardChanged | OnLeaderboardStatsChanged |
| Twitch.TwitchLeaderboardStats | StatsChanged | OnLeaderboardStatsChanged |
| Twitch.TwitchManager | ActionHistoryAdded | OnHistoryAdded |
| Twitch.TwitchManager | CommandsChanged | OnCommandsChanged |
| Twitch.TwitchManager | ConnectionStateChanged | OnTwitchConnectionStateChange |
| Twitch.TwitchManager | EventHistoryAdded | OnHistoryAdded |
| Twitch.TwitchManager | VoteHistoryAdded | OnHistoryAdded |
| TwitchDropAvailabilityManager | Updated | Action<TwitchDropAvailabilityManager> |
| UIOptions | OnOptionsVideoWindowChanged | Action<OptionsVideoWindowMode> |
| World | EntityLoadedDelegates | OnEntityLoadedDelegate |
| World | EntityUnloadedDelegates | OnEntityUnloadedDelegate |
| World | OnWorldChanged | OnWorldChangedEvent |
| XUi | OnBuilt | Action |
| XUi | OnShutdown | Action |
| XUiC_BasePartStack | SlotChangedEvent | XUiEvent_SlotChangedEventHandler |
| XUiC_BasePartStack | SlotChangingEvent | XUiEvent_SlotChangedEventHandler |
| XUiC_CategoryList | CategoryChanged | XUiEvent_CategoryChangedEventHandler |
| XUiC_CategoryList | CategoryClickChanged | XUiEvent_CategoryChangedEventHandler |
| XUiC_ColorPicker | OnSelectedColorChanged | XUiEvent_SelectedColorChanged |
| XUiC_ComboBox | OnHoveredStateChanged | XUiEvent_HoveredStateChanged |
| XUiC_ComboBox | OnValueChanged | XUiEvent_ValueChanged |
| XUiC_ComboBox | OnValueChangedGeneric | XUiEvent_GenericValueChanged |
| XUiC_Counter | OnCountChanged | XUiEvent_OnCountChanged |
| XUiC_DMBaseList | OnChildElementHovered | XUiEvent_OnHoverEventHandler |
| XUiC_DMBaseList | OnEntryClicked | XUiEvent_OnPressEventHandler |
| XUiC_DMBaseList | OnEntryDoubleClicked | XUiEvent_OnPressEventHandler |
| XUiC_DropDown | OnChangeHandler | XUiEvent_InputOnChangedEventHandler |
| XUiC_DropDown | OnSubmitHandler | XUiEvent_InputOnSubmitEventHandler |
| XUiC_EquipmentStack | SlotChangedEvent | XUiEvent_SlotChangedEventHandler |
| XUiC_ItemStack | LockChangedEvent | XUiEvent_LockChangeEventHandler |
| XUiC_ItemStack | SlotChangedEvent | XUiEvent_SlotChangedEventHandler |
| XUiC_ItemStack | TimeIntervalElapsedEvent | XUiEvent_TimeIntervalElapsedEventHandler |
| XUiC_ItemStack | ToolLockChangedEvent | XUiEvent_ToolLockChangeEventHandler |
| XUiC_List | ListEntryClicked | XUiEvent_ListEntryClickedEventHandler<T> |
| XUiC_List | PageContentsChanged | XUiEvent_PageContentsChangedEventHandler |
| XUiC_List | PageNumberChanged | XUiEvent_ListPageNumberChangedEventHandler |
| XUiC_List | SelectionChanged | XUiEvent_ListSelectionChangedEventHandler<T> |
| XUiC_MessageBoxWindowGroup | OnLeftButtonEvent | Action |
| XUiC_MessageBoxWindowGroup | OnRightButtonEvent | Action |
| XUiC_OptionsAudio | OnSettingsChanged | Action |
| XUiC_OptionsController | OnSettingsChanged | Action |
| XUiC_OptionsControls | OnSettingsChanged | Action |
| XUiC_OptionsGeneral | OnSettingsChanged | Action |
| XUiC_OptionsSelector | OnSelectionChanged | XUiEvent_OnOptionSelectionChanged |
| XUiC_OptionsTwitch | OnSettingsChanged | Action |
| XUiC_OptionsVideo | OnSettingsChanged | Action |
| XUiC_OptionsVideoSimplified | OnSettingsChanged | Action |
| XUiC_Paging | OnPageChanged | XUiEvent_PageChangedEventHandler |
| XUiC_PrefabFeatureEditorList | FeatureChanged | FeatureChangedDelegate |
| XUiC_PrefabFileList | OnEntryDoubleClicked | EntryDoubleClickedDelegate |
| XUiC_RecipeList | PageNumberChanged | XUiEvent_PageNumberChangedEventHandler |
| XUiC_RecipeList | RecipeChanged | XUiEvent_RecipeChangedEventHandler |
| XUiC_RequiredItemStack | FailedSwap | XUiEvent_RequiredSlotFailedSwapEventHandler |
| XUiC_SavegamesList | OnEntryDoubleClicked | XUiEvent_OnPressEventHandler |
| XUiC_Selector | OnSelectedIndexChanged | XUiEvent_SelectedIndexChanged |
| XUiC_ServerFilters | OnFilterChanged | Action<IServerBrowserFilterControl> |
| XUiC_ServersList | CountsChanged | Action |
| XUiC_ServersList | OnEntryDoubleClicked | XUiEvent_OnPressEventHandler |
| XUiC_ServersList | OnFilterResultsChanged | Action<int> |
| XUiC_SimpleButton | OnHovered | XUiEvent_OnHoverEventHandler |
| XUiC_SimpleButton | OnPressed | XUiEvent_OnPressEventHandler |
| XUiC_Slider | OnValueChanged | XUiEvent_SliderValueChanged |
| XUiC_SlotSelector | OnSelectedSlotChanged | XUiEvent_SelectedSlotChanged |
| XUiC_SpawnNearFriendsList | SpawnClicked | Action<PersistentPlayerData> |
| XUiC_TabSelector | OnTabChanged | TabChangedDelegate |
| XUiC_TextInput | OnChangeHandler | XUiEvent_InputOnChangedEventHandler |
| XUiC_TextInput | OnClipboardHandler | UIInput.OnClipboard |
| XUiC_TextInput | OnInputAbortedHandler | XUiEvent_InputOnAbortedEventHandler |
| XUiC_TextInput | OnInputErrorHandler | XUiEvent_InputOnErrorEventHandler |
| XUiC_TextInput | OnInputSelectedHandler | XUiEvent_InputOnSelectedEventHandler |
| XUiC_TextInput | OnSubmitHandler | XUiEvent_InputOnSubmitEventHandler |
| XUiC_ToggleButton | OnValueChanged | XUiEvent_ToggleButtonValueChanged |
| XUiC_WorkstationFuelGrid | OnWorkstationFuelChanged | XuiEvent_WorkstationItemsChanged |
| XUiC_WorkstationMaterialInputWindow | OnWorkstationMaterialWeightsChanged | XuiEvent_WorkstationItemsChanged |
| XUiC_WorkstationToolGrid | OnWorkstationToolsChanged | XuiEvent_WorkstationItemsChanged |
| XUiC_WorldGenerationWindowGroup | OnCountyNameChanged | Action |
| XUiC_WorldGenerationWindowGroup | OnWorldSizeChanged | Action |
| XUiC_WorldList | OnEntryClicked | XUiEvent_OnPressEventHandler |
| XUiC_WorldList | OnEntryDoubleClicked | XUiEvent_OnPressEventHandler |
| XUiController | OnDoubleClick | XUiEvent_OnPressEventHandler |
| XUiController | OnDrag | XUiEvent_OnDragEventHandler |
| XUiController | OnHold | XUiEvent_OnHeldHandler |
| XUiController | OnHover | XUiEvent_OnHoverEventHandler |
| XUiController | OnPress | XUiEvent_OnPressEventHandler |
| XUiController | OnRightPress | XUiEvent_OnPressEventHandler |
| XUiController | OnScroll | XUiEvent_OnScrollEventHandler |
| XUiController | OnSelect | XUiEvent_OnSelectEventHandler |
| XUiController | OnVisiblity | XUiEvent_OnVisibilityChanged |
| XUiEventManager | OnSkillExperienceAdded | XUiEvent_SkillExperienceAdded |
| XUiM_PlayerEquipment | HandleRefreshEquipment | XUiEvent_RefreshEquipment |
| XUiM_PlayerEquipment | SlotChanged | XUiEvent_EquipmentSlotChanged |
| XUiM_PlayerInventory | OnBackpackItemsChanged | XUiEvent_BackpackItemsChanged |
| XUiM_PlayerInventory | OnCurrencyChanged | XUiEvent_CurrencyChanged |
| XUiM_PlayerInventory | OnToolbeltItemsChanged | XUiEvent_ToolbeltItemsChanged |
| XUiM_Quest | OnTrackedChallengeChanged | XUiEvent_TrackedQuestChanged |
| XUiM_Quest | OnTrackedQuestChanged | XUiEvent_TrackedQuestChanged |
| XUiM_Recipes | OnTrackedRecipeChanged | XUiEvent_TrackedQuestChanged |
| XUiV_Grid | OnSizeChanged | UIGrid.OnSizeChanged |
| XUiV_Grid | OnSizeChangedSimple | Action |


## Event Subscriptions

Who subscribes to which events:

| Subscriber | Event Owner | Event | Handler |
|------------|-------------|-------|---------|
| GameManager | ? | ContactEvent | PhysicsContactEvent |
| PlayerActionsLocal | ? | OnActiveDeviceChanged | <lambda> |
| Platform.AbsPlatform | ? | OnDeviceDetached | ControllerDisconnected |
| Platform.PlayerInputManager | ? | OnLogMessage | <lambda> |
| EntityPlayerLocal | Bag | OnBackpackItemsChangedInternal | callInventoryChanged |
| GameManager | Bag | OnBackpackItemsChangedInternal | <lambda> |
| ObjectiveFetch | Bag | OnBackpackItemsChangedInternal | Backpack_OnBackpackItemsChangedInternal |
| ObjectiveFetch | Bag | OnBackpackItemsChangedInternal | Backpack_OnBackpackItemsChangedInternal |
| ObjectiveFetchAnyContainer | Bag | OnBackpackItemsChangedInternal | Backpack_OnBackpackItemsChangedInternal |
| ObjectiveFetchAnyContainer | Bag | OnBackpackItemsChangedInternal | Backpack_OnBackpackItemsChangedInternal |
| ObjectiveFetchFromContainer | Bag | OnBackpackItemsChangedInternal | Backpack_OnBackpackItemsChangedInternal |
| ObjectiveFetchFromContainer | Bag | OnBackpackItemsChangedInternal | Backpack_OnBackpackItemsChangedInternal |
| ObjectiveFetchFromTreasure | Bag | OnBackpackItemsChangedInternal | Backpack_OnBackpackItemsChangedInternal |
| ObjectiveFetchFromTreasure | Bag | OnBackpackItemsChangedInternal | Backpack_OnBackpackItemsChangedInternal |
| XUiC_VehicleContainer | Bag | OnBackpackItemsChangedInternal | OnBagItemChangedInternal |
| XUiC_VehicleContainer | Bag | OnBackpackItemsChangedInternal | OnBagItemChangedInternal |
| XUiM_PlayerInventory | Bag | OnBackpackItemsChangedInternal | onBackpackItemsChanged |
| XUiM_PlayerInventory | Bag | OnBackpackItemsChangedInternal | onBackpackItemsChanged |
| Challenges.ChallengeObjectiveGather | Bag | OnBackpackItemsChangedInternal | ItemsChangedInternal |
| Challenges.ChallengeObjectiveGather | Bag | OnBackpackItemsChangedInternal | ItemsChangedInternal |
| Challenges.ChallengeObjectiveGather | Bag | OnBackpackItemsChangedInternal | ItemsChangedInternal |
| Challenges.ChallengeObjectiveGatherByTag | Bag | OnBackpackItemsChangedInternal | ItemsChangedInternal |
| Challenges.ChallengeObjectiveGatherByTag | Bag | OnBackpackItemsChangedInternal | ItemsChangedInternal |
| Challenges.ChallengeObjectiveGatherByTag | Bag | OnBackpackItemsChangedInternal | ItemsChangedInternal |
| Challenges.ChallengeObjectiveGatherIngredient | Bag | OnBackpackItemsChangedInternal | ItemsChangedInternal |
| Challenges.ChallengeObjectiveGatherIngredient | Bag | OnBackpackItemsChangedInternal | ItemsChangedInternal |
| Challenges.ChallengeObjectiveGatherIngredient | Bag | OnBackpackItemsChangedInternal | ItemsChangedInternal |
| XUiC_QuestObjectiveEntry | BaseObjective | ValueChanged | Objective_ValueChanged |
| XUiC_QuestObjectiveEntry | BaseObjective | ValueChanged | Objective_ValueChanged |
| XUiC_QuestTrackerObjectiveEntry | BaseObjective | ValueChanged | Objective_ValueChanged |
| XUiC_QuestTrackerObjectiveEntry | BaseObjective | ValueChanged | Objective_ValueChanged |
| XUiC_QuestTrackerObjectiveEntry | Challenges.BaseChallengeObjective | ValueChanged | Objective_ValueChanged |
| XUiC_QuestTrackerObjectiveEntry | Challenges.BaseChallengeObjective | ValueChanged | Objective_ValueChanged |
| XUiC_QuestTrackerObjectiveList | Challenges.Challenge | OnChallengeStateChanged | CurrentChallenge_OnChallengeStateChanged |
| XUiC_QuestTrackerObjectiveList | Challenges.Challenge | OnChallengeStateChanged | CurrentChallenge_OnChallengeStateChanged |
| XUiC_QuestTrackerWindow | Challenges.Challenge | OnChallengeStateChanged | CurrentChallenge_OnChallengeStateChanged |
| XUiC_QuestTrackerWindow | Challenges.Challenge | OnChallengeStateChanged | CurrentChallenge_OnChallengeStateChanged |
| XUiC_QuestTrackerWindow | Challenges.Challenge | OnChallengeStateChanged | CurrentChallenge_OnChallengeStateChanged |
| AstarManager | ChunkCluster | OnBlockChangedDelegates | OnBlockChanged |
| AstarManager | ChunkCluster | OnBlockChangedDelegates | OnBlockChanged |
| BlockToolSelection | ChunkCluster | OnBlockChangedDelegates | undoBlockChangeDelegate |
| BlockToolSelection | ChunkCluster | OnBlockChangedDelegates | undoBlockChangeDelegate |
| PrefabEditModeManager | ChunkCluster | OnBlockChangedDelegates | blockChangeDelegate |
| PrefabEditModeManager | ChunkCluster | OnBlockChangedDelegates | blockChangeDelegate |
| AstarManager | ChunkCluster | OnBlockDamagedDelegates | OnBlockDamaged |
| AstarManager | ChunkCluster | OnBlockDamagedDelegates | OnBlockDamaged |
| WorldEnvironment | ChunkCluster | OnChunkVisibleDelegates | chunkClusterVisibleDelegate |
| AntiCheatEncryptionAuthServer | ConnectionManager | OnClientDisconnected | OnClientDisconnected |
| AntiCheatEncryptionAuthServer | ConnectionManager | OnClientDisconnected | OnClientDisconnected |
| DynamicMeshManager | ConnectionManager | OnClientDisconnected | OnClientDisconnect |
| DynamicMeshManager | ConnectionManager | OnClientDisconnected | OnClientDisconnect |
| DynamicMusic.Conductor | ConnectionManager | OnClientDisconnected | OnClientDisconnected |
| DynamicMusic.Conductor | ConnectionManager | OnClientDisconnected | OnClientDisconnected |
| Platform.ClientLobbyManager | ConnectionManager | OnClientDisconnected | OnClientDisconnected |
| Platform.Steam.AuthenticationClient | ConnectionManager | OnDisconnectFromServer | OnDisconnectFromServer |
| XUiC_CraftingListInfo | CraftingManager | RecipeUnlocked | CraftingManager_RecipeUnlocked |
| XUiC_CraftingListInfo | CraftingManager | RecipeUnlocked | CraftingManager_RecipeUnlocked |
| EntityPlayerLocal | DataItem<bool> | OnChangeDelegates | OnWeatherGodModeChanged |
| DiscordManager | DiscordManager | ActivityInviteReceived | <lambda> |
| XUiC_DiscordPendingList | DiscordManager | ActivityInviteReceived | discordActivityInviteReceived |
| XUiC_DiscordPendingList | DiscordManager | ActivityInviteReceived | discordActivityInviteReceived |
| XUiC_DiscordMainMenuFriends | DiscordManager | ActivityJoining | discordActivityJoining |
| XUiC_DiscordMainMenuFriends | DiscordManager | ActivityJoining | discordActivityJoining |
| XUiC_OptionsAudio | DiscordManager | AudioDevicesChanged | DiscordOnAudioDevicesChanged |
| XUiC_OptionsAudio | DiscordManager | AudioDevicesChanged | DiscordOnAudioDevicesChanged |
| XUiC_DiscordLobbyMemberList | DiscordManager | CallChanged | InstanceOnCallChanged |
| XUiC_DiscordLobbyMemberList | DiscordManager | CallChanged | InstanceOnCallChanged |
| XUiC_DiscordVoiceControls | DiscordManager | CallChanged | onCallChanged |
| XUiC_DiscordLobbyMemberList | DiscordManager | CallMembersChanged | InstanceOnCallMembersChanged |
| XUiC_DiscordLobbyMemberList | DiscordManager | CallMembersChanged | InstanceOnCallMembersChanged |
| XUiC_DiscordLobbyControl | DiscordManager | CallStatusChanged | onCallStatusChanged |
| XUiC_DiscordVoiceControls | DiscordManager | CallStatusChanged | onCallStatusChanged |
| DiscordManager | DiscordManager | FriendsListChanged | updatePendingActionsEvent |
| XUiC_DiscordBlockedUsersList | DiscordManager | FriendsListChanged | discordFriendsListChanged |
| XUiC_DiscordBlockedUsersList | DiscordManager | FriendsListChanged | discordFriendsListChanged |
| XUiC_DiscordFriendsList | DiscordManager | FriendsListChanged | discordFriendsListChanged |
| XUiC_DiscordFriendsList | DiscordManager | FriendsListChanged | discordFriendsListChanged |
| XUiC_DiscordLobbyMemberList | DiscordManager | LobbyMembersChanged | InstanceOnLobbyMembersChanged |
| XUiC_DiscordLobbyMemberList | DiscordManager | LobbyMembersChanged | InstanceOnLobbyMembersChanged |
| XUiC_DiscordLobbyControl | DiscordManager | LobbyStateChanged | onLobbyStateChanged |
| XUiC_DiscordVoiceControls | DiscordManager | LobbyStateChanged | onLobbyStateChanged |
| XUiC_OptionsAudio | DiscordManager | LocalUserChanged | discordLocalUserChanged |
| XUiC_OptionsAudio | DiscordManager | LocalUserChanged | discordLocalUserChanged |
| XUiC_DiscordMainMenuButton | DiscordManager | PendingActionsUpdate | discordPendingActionsUpdate |
| XUiC_DiscordMainMenuButton | DiscordManager | PendingActionsUpdate | discordPendingActionsUpdate |
| XUiC_DiscordMainMenuFriends | DiscordManager | PendingActionsUpdate | discordPendingActionsUpdate |
| XUiC_DiscordMainMenuFriends | DiscordManager | PendingActionsUpdate | discordPendingActionsUpdate |
| DiscordManager | DiscordManager | RelationshipChanged | <lambda> |
| XUiC_DiscordPendingList | DiscordManager | RelationshipChanged | discordRelationshipChanged |
| XUiC_DiscordPendingList | DiscordManager | RelationshipChanged | discordRelationshipChanged |
| XUiC_DiscordVoiceControls | DiscordManager | SelfMuteStateChanged | onSelfMuteStateChanged |
| XUiC_DiscordFriendsList | DiscordManager | StatusChanged | discordStatusChanged |
| XUiC_DiscordFriendsList | DiscordManager | StatusChanged | discordStatusChanged |
| XUiC_DiscordMainMenuButton | DiscordManager | StatusChanged | discordStatusChanged |
| XUiC_DiscordMainMenuButton | DiscordManager | StatusChanged | discordStatusChanged |
| XUiC_DiscordPendingList | DiscordManager | StatusChanged | discordStatusChanged |
| XUiC_DiscordPendingList | DiscordManager | StatusChanged | discordStatusChanged |
| XUiC_OptionsAudio | DiscordManager | StatusChanged | discordStatusChanged |
| XUiC_OptionsAudio | DiscordManager | StatusChanged | discordStatusChanged |
| XUiC_DiscordLogin | DiscordManager | UserAuthorizationResult | authResult |


## Event Fires

Where events are triggered in game code:

| Firing Type | Event Owner | Event | Method |
|-------------|-------------|-------|--------|
| AstarManager | AstarManager | OriginChanged | direct |
| AutoTurretFireController | AutoTurretFireController | OnPoweredOff | direct |
| AvatarController | AvatarController | CancelEvent | direct |
| AvatarController | AvatarController | OnTrigger | direct |
| AvatarController | AvatarController | TriggerEvent | direct |
| AvatarMultiBodyController | AvatarMultiBodyController | OnTrigger | direct |
| AvatarMultiBodyController | AvatarMultiBodyController | avatarVisibilityChanged | direct |
| Bag | Bag | onBackpackChanged | direct |
| Bag | Bag | onBackpackChanged | direct |
| Bag | Bag | onBackpackChanged | direct |
| Bag | Bag | onBackpackChanged | direct |
| Bag | Bag | onBackpackChanged | direct |
| Bag | Bag | onBackpackChanged | direct |
| BaseQuestData | BaseQuestData | OnAdd | direct |
| BaseQuestData | BaseQuestData | OnRemove | direct |
| BaseQuestData | BaseQuestData | OnRemove | direct |
| Block | Block | OnBlockActivated | direct |
| Block | Block | OnBlockDamaged | direct |
| Block | Block | OnBlockDestroyedBy | direct |
| BlockCarExplodeLoot | BlockCarExplodeLoot | OnBlockActivated | direct |
| BlockCollector | BlockCollector | OnBlockActivated | direct |
| BlockCompositeTileEntity | BlockCompositeTileEntity | OnBlockActivated | direct |
| BlockCompositeTileEntity | BlockCompositeTileEntity | OnBlockDestroyedBy | direct |
| BlockCompositeTileEntity | BlockCompositeTileEntity | OnBlockValueChanged | direct |
| BlockCropsGrown | BlockCropsGrown | OnBlockActivated | direct |
| BlockDoor | BlockDoor | OnBlockActivated | direct |
| BlockDoor | BlockDoor | OnBlockActivated | direct |
| BlockDoor | BlockDoor | OnBlockActivated | direct |
| BlockDoorSecure | BlockDoorSecure | OnBlockActivated | direct |
| BlockDoorSecure | BlockDoorSecure | OnBlockActivated | direct |
| BlockDoorSecure | BlockDoorSecure | OnBlockActivated | direct |
| BlockLandClaim | BlockLandClaim | OnBlockActivated | direct |
| BlockLandClaim | BlockLandClaim | OnBlockActivated | direct |
| BlockLauncher | BlockLauncher | OnBlockActivated | direct |
| BlockLoot | BlockLoot | OnBlockActivated | direct |
| BlockModelTree | BlockModelTree | OnBlockDestroyedBy | direct |
| BlockMotionSensor | BlockMotionSensor | OnBlockActivated | direct |
| BlockPlayerSign | BlockPlayerSign | OnBlockActivated | direct |
| BlockPlayerSign | BlockPlayerSign | OnBlockActivated | direct |
| BlockPlayerSign | BlockPlayerSign | OnBlockActivated | direct |
| BlockPowerSource | BlockPowerSource | OnBlockActivated | direct |
| BlockPowered | BlockPowered | OnBlockActivated | direct |
| BlockPoweredDoor | BlockPoweredDoor | OnBlockActivated | direct |
| BlockPressurePlate | BlockPressurePlate | OnBlockActivated | direct |
| BlockRanged | BlockRanged | OnBlockActivated | direct |
| BlockSecureLoot | BlockSecureLoot | OnBlockActivated | direct |
| BlockSecureLoot | BlockSecureLoot | OnBlockActivated | direct |
| BlockSecureLoot | BlockSecureLoot | OnBlockActivated | direct |
| BlockSecureLootSigned | BlockSecureLootSigned | OnBlockActivated | direct |
| BlockSecureLootSigned | BlockSecureLootSigned | OnBlockActivated | direct |
| BlockSecureLootSigned | BlockSecureLootSigned | OnBlockActivated | direct |
| BlockSecureLootSigned | BlockSecureLootSigned | OnBlockActivated | direct |
| BlockSpotlight | BlockSpotlight | OnBlockActivated | direct |
| BlockTimerRelay | BlockTimerRelay | OnBlockActivated | direct |
| BlockTripWire | BlockTripWire | OnBlockActivated | direct |
| BlockVendingMachine | BlockVendingMachine | OnBlockActivated | direct |
| BlockVendingMachine | BlockVendingMachine | OnBlockActivated | direct |
| BlockVendingMachine | BlockVendingMachine | OnBlockActivated | direct |
| BlockWorkstation | BlockWorkstation | OnBlockActivated | direct |
| ChallengeJournal | ChallengeJournal | FireEvent | direct |
| Challenges.BaseChallengeObjective | Challenges.BaseChallengeObjective | HandleValueChanged | direct |
| Challenges.BaseChallengeObjective | Challenges.BaseChallengeObjective | HandleValueChanged | direct |
| Challenges.ChallengeObjectiveGather | Challenges.ChallengeObjectiveGather | ItemsChangedInternal | direct |
| Challenges.ChallengeObjectiveGatherByTag | Challenges.ChallengeObjectiveGatherByTag | ItemsChangedInternal | direct |
| Challenges.ChallengeObjectiveTime | Challenges.ChallengeObjectiveTime | HandleValueChanged | direct |
| ChunkProviderGenerateWorld | ChunkProviderGenerateWorld | OnChunkSyncedAndDecorated | direct |
| ChunkProviderGenerateWorld | ChunkProviderGenerateWorld | OnChunkSyncedAndDecorated | direct |
| CursorControllerAbs | CursorControllerAbs | OnVirtualCursorVisibleChanged | direct |
| DeferredNightVisionEffect | DeferredNightVisionEffect | OnDisable | direct |
| DiscordManager | DiscordManager | OnLobbyMemberChanged | direct |
| DiscordManager | DiscordManager | OnLobbyMemberChanged | direct |
| DiscordManager | DiscordManager | OnLobbyMemberChanged | direct |
| DiscordManager | DiscordManager | localDiscordOrEntityIdChanged | direct |
| DiscordManager | DiscordManager | localDiscordOrEntityIdChanged | direct |
| DiscordManager | DiscordManager | updatePendingActionsEvent | direct |
| DiscordManager | DiscordManager | updatePendingActionsEvent | direct |
| DynamicMeshManager | DynamicMeshManager | ChunkChanged | direct |
| DynamicMeshManager | DynamicMeshManager | OnWorldUnload | direct |
| DynamicMeshManager | DynamicMeshManager | OnWorldUnload | direct |
| DynamicMusic.Legacy.FrequencyManager | DynamicMusic.Legacy.FrequencyManager | OnMusicStarted | direct |
| DynamicMusic.Legacy.FrequencyManager | DynamicMusic.Legacy.FrequencyManager | OnMusicStopped | direct |
| DynamicMusic.Legacy.LayerStreamer | DynamicMusic.Legacy.LayerStreamer | OnClipSetLoad | direct |
| DynamicPrefabDecorator | DynamicPrefabDecorator | CallPrefabRemovedEvent | direct |
| DynamicPrefabDecorator | DynamicPrefabDecorator | CallPrefabRemovedEvent | direct |
| Entity | Entity | OnHeadUnderwaterStateChanged | direct |
| Entity | Entity | OnPushEntity | direct |
| Entity | Entity | SwimChanged | direct |
| EntityAlive | EntityAlive | FireEvent | direct |
| EntityAlive | EntityAlive | FireEvent | direct |
| EntityAlive | EntityAlive | FireEvent | direct |
| EntityAlive | EntityAlive | FireEvent | direct |
| EntityAlive | EntityAlive | FireEvent | direct |
| EntityAlive | EntityAlive | FireEvent | direct |
| EntityAlive | EntityAlive | OnDeathUpdate | direct |
| EntityAlive | EntityAlive | OnEntityDeath | direct |
| EntityAlive | EntityAlive | OnEntityDeath | direct |
| EntityAlive | EntityAlive | OnEntityTargeted | direct |
| EntityAlive | EntityAlive | OnUpdateLive | direct |
| EntityAlive | EntityAlive | onSpawnStateChanged | direct |
| EntityBuffs | EntityBuffs | FireEvent | direct |
| EntityBuffs | EntityBuffs | FireEvent | direct |
| EntityBuffs | EntityBuffs | FireEvent | direct |
| EntityBuffs | EntityBuffs | FireEvent | direct |
| EntityBuffs | EntityBuffs | FireEvent | direct |
| EntityBuffs | EntityBuffs | FireEvent | direct |
| EntityBuffs | EntityBuffs | FireEvent | direct |
| EntityPlayerLocal | EntityPlayerLocal | FireEvent | direct |
| EntityPlayerLocal | EntityPlayerLocal | FireEvent | direct |
| EntityPlayerLocal | EntityPlayerLocal | FireEvent | direct |
| EntityPlayerLocal | EntityPlayerLocal | FireEvent | direct |
| EntityPlayerLocal | EntityPlayerLocal | FireEvent | direct |
| EntityPlayerLocal | EntityPlayerLocal | FireEvent | direct |
| EntityPlayerLocal | EntityPlayerLocal | FireEvent | direct |
| EntityPlayerLocal | EntityPlayerLocal | FireEvent | direct |
| EntityPlayerLocal | EntityPlayerLocal | FireEvent | direct |
| EntityPlayerLocal | EntityPlayerLocal | FireEvent | direct |
| EntityPlayerLocal | EntityPlayerLocal | FireEvent | direct |
| EntityPlayerLocal | EntityPlayerLocal | FireEvent | direct |
| EntityPlayerLocal | EntityPlayerLocal | FireEvent | direct |
| EntityPlayerLocal | EntityPlayerLocal | FireEvent | direct |
| EntityPlayerLocal | EntityPlayerLocal | FireEvent | direct |
| EntityPlayerLocal | EntityPlayerLocal | FireEvent | direct |
| EntityPlayerLocal | EntityPlayerLocal | FireEvent | direct |
| EntityPlayerLocal | EntityPlayerLocal | FireEvent | direct |
| EntityPlayerLocal | EntityPlayerLocal | FireEvent | direct |
| EntityPlayerLocal | EntityPlayerLocal | FireEvent | direct |
| EntityPlayerLocal | EntityPlayerLocal | FireEvent | direct |
| EntityPlayerLocal | EntityPlayerLocal | FireEvent | direct |
| EntityPlayerLocal | EntityPlayerLocal | FireEvent | direct |
| EntityPlayerLocal | EntityPlayerLocal | FireEvent | direct |
| EventsFromXml | EventsFromXml | parseEvent | direct |
| GameEvent.SequenceActions.ActionBaseClientAction | GameEvent.SequenceActions.ActionBaseClientAction | OnClientPerform | direct |
| GameEvent.SequenceActions.ActionBaseClientAction | GameEvent.SequenceActions.ActionBaseClientAction | OnServerPerform | direct |
| GameEvent.SequenceActions.ActionBaseItemAction | GameEvent.SequenceActions.ActionBaseItemAction | OnClientActionEnded | direct |
| GameEvent.SequenceActions.ActionBaseItemAction | GameEvent.SequenceActions.ActionBaseItemAction | OnClientActionStarted | direct |
| GameEvent.SequenceActions.ActionPlaySound | GameEvent.SequenceActions.ActionPlaySound | OnClientPerform | direct |
| GameEvent.SequenceActions.BaseAction | GameEvent.SequenceActions.BaseAction | OnInit | direct |
| GameEvent.SequenceActions.BaseAction | GameEvent.SequenceActions.BaseAction | OnPerformAction | direct |
| GameEvent.SequenceActions.BaseAction | GameEvent.SequenceActions.BaseAction | OnReset | direct |
| GameEvent.SequenceRequirements.BaseRequirement | GameEvent.SequenceRequirements.BaseRequirement | OnInit | direct |
| GameEventManager | GameEventManager | HandleFlagChanged | direct |
| GameEventManager | GameEventManager | HandleFlagChanged | direct |
| GameEventManager | GameEventManager | HandleFlagChanged | direct |
| GameManager | GameManager | OnApplicationResume | direct |
| GameManager | GameManager | PersistentPlayerEvent | direct |
| GameManager | GameManager | PersistentPlayerEvent | direct |
| Inventory | Inventory | HoldingItemHasChanged | direct |
| Inventory | Inventory | HoldingItemHasChanged | direct |
| Inventory | Inventory | HoldingItemHasChanged | direct |
| Inventory | Inventory | HoldingItemHasChanged | direct |
| Inventory | Inventory | HoldingItemHasChanged | direct |
| Inventory | Inventory | HoldingItemHasChanged | direct |
| Inventory | Inventory | onInventoryChanged | direct |
| Inventory | Inventory | onInventoryChanged | direct |
| ItemAction | ItemAction | OnModificationsChanged | direct |
| ItemActionReplaceBlock | ItemActionReplaceBlock | checkBlockCanBeChanged | direct |
| ItemActionTextureBlock | ItemActionTextureBlock | checkBlockCanBeChanged | direct |
| ItemActionTextureBlock | ItemActionTextureBlock | checkBlockCanBeChanged | direct |
| ItemClassTimeBomb | ItemClassTimeBomb | OnHoldingReset | direct |
| LocalPlayer | LocalPlayer | DispatchLocalPlayersChanged | direct |
| LocalPlayer | LocalPlayer | DispatchLocalPlayersChanged | direct |
| LocalPlayerUI | LocalPlayerUI | DispatchLocalPlayersChanged | direct |
| LocalPlayerUI | LocalPlayerUI | DispatchLocalPlayersChanged | direct |
| NGuiUIFollowTarget | NGuiUIFollowTarget | OnUpdate | direct |
| NetworkServerLiteNetLib | NetworkServerLiteNetLib | OnPlayerDisconnected | direct |
| NetworkServerLiteNetLib | NetworkServerLiteNetLib | OnPlayerDisconnected | direct |
| ObjectiveRandomGoto | ObjectiveRandomGoto | OnStart | direct |
| ObjectiveTreasureChest | ObjectiveTreasureChest | RadiusBoundsChanged | direct |
| ObjectiveTreasureChest | ObjectiveTreasureChest | RadiusBoundsChanged | direct |
| ObjectiveTreasureChest | ObjectiveTreasureChest | RadiusBoundsChanged | direct |
| ObservableDictionary | ObservableDictionary | OnEntryAdded | direct |
| ObservableDictionary | ObservableDictionary | OnEntryAdded | direct |
| ObservableDictionary | ObservableDictionary | OnEntryModified | direct |
| ObservableDictionary | ObservableDictionary | OnEntryModified | direct |
| ObservableDictionary | ObservableDictionary | OnEntryModified | direct |
| ObservableDictionary | ObservableDictionary | OnEntryRemoved | direct |
| ObservableDictionary | ObservableDictionary | OnEntryUpdated | direct |
| PartyVoice | PartyVoice | gamePrefChanged | direct |
| PartyVoice | PartyVoice | gamePrefChanged | direct |
| PartyVoice | PartyVoice | gamePrefChanged | direct |
| Platform.EOS.NetworkClientEos | Platform.EOS.NetworkClientEos | OnConnectedToServer | direct |
| Platform.EOS.NetworkClientEos | Platform.EOS.NetworkClientEos | OnDisconnectedFromServer | direct |
| Platform.EOS.NetworkServerEos | Platform.EOS.NetworkServerEos | OnPlayerConnected | direct |
| Platform.EOS.NetworkServerEos | Platform.EOS.NetworkServerEos | OnPlayerDisconnected | direct |
| Platform.EOS.NetworkServerEos | Platform.EOS.NetworkServerEos | OnPlayerDisconnected | direct |
| Platform.EOS.SanctionsCheck | Platform.EOS.SanctionsCheck | OnSanctionsQueryResolveAndGatherSanctions | direct |
| Platform.EOS.SanctionsCheck | Platform.EOS.SanctionsCheck | OnSanctionsQueryResolveAndGatherSanctions | direct |
| Platform.EOS.User | Platform.EOS.User | OnResume | direct |
| Platform.EOS.User | Platform.EOS.User | OnResumeRefreshLoginCoroutine | direct |
| Platform.EOS.User | Platform.EOS.User | OnSuspend | direct |
| Platform.EOS.Voice | Platform.EOS.Voice | OnPartyVoiceInitialized | direct |
| Platform.EOS.Voice | Platform.EOS.Voice | OnPartyVoiceUninitialize | direct |
| Platform.LAN.LANServerList | Platform.LAN.LANServerList | OnServerFound | direct |
| Platform.PlatformUserBlockedData | Platform.PlatformUserBlockedData | OnBlockedStateChanged | direct |
| Platform.PlatformUserData | Platform.PlatformUserData | OnUserAdded | direct |
| Platform.PlatformUserManager | Platform.PlatformUserManager | OnUserAdded | direct |
| Platform.PlatformUserManager | Platform.PlatformUserManager | OnUserAdded | direct |
| Platform.Steam.AchievementManager | Platform.Steam.AchievementManager | SendAchievementEvent | direct |
| Platform.Steam.AchievementManager | Platform.Steam.AchievementManager | SendAchievementEvent | direct |
| Platform.Steam.AuthenticationClient | Platform.Steam.AuthenticationClient | OnDisconnectFromServer | direct |
| Platform.Steam.NetworkClientSteam | Platform.Steam.NetworkClientSteam | OnConnectedToServer | direct |
| Platform.Steam.NetworkClientSteam | Platform.Steam.NetworkClientSteam | OnDisconnectedFromServer | direct |
| Platform.Steam.NetworkServerSteam | Platform.Steam.NetworkServerSteam | OnPlayerConnected | direct |
| Platform.Steam.NetworkServerSteam | Platform.Steam.NetworkServerSteam | OnPlayerDisconnected | direct |
| Platform.Steam.NetworkServerSteam | Platform.Steam.NetworkServerSteam | OnPlayerDisconnected | direct |
| Platform.Steam.SteamQueryPortReader | Platform.Steam.SteamQueryPortReader | RunGameServerDetailsEvent | direct |
| Platform.Steam.SteamQueryPortReader | Platform.Steam.SteamQueryPortReader | RunGameServerDetailsEvent | direct |
| Platform.Steam.SteamQueryPortReader | Platform.Steam.SteamQueryPortReader | RunGameServerDetailsEvent | direct |
| Platform.Steam.SteamQueryPortReader | Platform.Steam.SteamQueryPortReader | RunGameServerDetailsEvent | direct |
| Platform.Steam.SteamQueryPortReader | Platform.Steam.SteamQueryPortReader | RunGameServerDetailsEvent | direct |
| Platform.Steam.SteamQueryPortReader | Platform.Steam.SteamQueryPortReader | RunGameServerDetailsEvent | direct |
| Platform.Steam.SteamQueryPortReader | Platform.Steam.SteamQueryPortReader | RunGameServerDetailsEvent | direct |
| PowerBatteryBank | PowerBatteryBank | IsPoweredChanged | direct |
| PowerConsumerToggle | PowerConsumerToggle | IsPoweredChanged | direct |
| PowerItem | PowerItem | IsPoweredChanged | direct |
| PowerItem | PowerItem | IsPoweredChanged | direct |
| Progression | Progression | FireEvent | direct |
| QuestsFromXml | QuestsFromXml | ParseEvent | direct |
| RegionFileManager | RegionFileManager | OnGamePrefChanged | direct |
| RegionFileManager | RegionFileManager | OnGamePrefChanged | direct |
| SkyManager | SkyManager | OnShadowDistanceChanged | direct |
| SleeperVolumeToolManager | SleeperVolumeToolManager | SelectionChanged | direct |
| SleeperVolumeToolManager | SleeperVolumeToolManager | SelectionChanged | direct |
| SpinningBladeTrapController | SpinningBladeTrapController | CheckHealthChanged | direct |
| SpinningBladeTrapController | SpinningBladeTrapController | CheckHealthChanged | direct |
| TaskManager | TaskManager | OnTaskCompleted | direct |
| TaskManager | TaskManager | OnTaskCompleted | direct |
| TaskManager | TaskManager | OnTaskCreated | direct |
| TaskManager | TaskManager | OnTaskCreated | direct |
| TileEntity | TileEntity | OnDestroy | direct |
| TileEntity | TileEntity | OnSetLocalChunkPosition | direct |
| TileEntityCollector | TileEntityCollector | HandleModChanged | direct |
| TileEntityCollector | TileEntityCollector | HandleModChanged | direct |
| TileEntityCollector | TileEntityCollector | HandleModChanged | direct |
| TileEntityCollector | TileEntityCollector | OnDestroy | direct |
| TileEntityCollector | TileEntityCollector | emitHeatMapEvent | direct |
| TileEntityForge | TileEntityForge | emitHeatMapEvent | direct |
| TileEntityLootContainer | TileEntityLootContainer | OnDestroy | direct |
| TileEntityWorkstation | TileEntityWorkstation | OnSetLocalChunkPosition | direct |
| TileEntityWorkstation | TileEntityWorkstation | emitHeatMapEvent | direct |
| TileEntityWorkstation | TileEntityWorkstation | emitHeatMapEvent | direct |
| Twitch.BaseTwitchVoteRequirement | Twitch.BaseTwitchVoteRequirement | OnInit | direct |
| Twitch.TwitchAction | Twitch.TwitchAction | OnInit | direct |
| Twitch.TwitchAction | Twitch.TwitchAction | OnPerformAction | direct |
| Twitch.TwitchLeaderboardStats | Twitch.TwitchLeaderboardStats | HandleLeaderboardChanged | direct |
| Twitch.TwitchLeaderboardStats | Twitch.TwitchLeaderboardStats | HandleLeaderboardChanged | direct |
| Twitch.TwitchLeaderboardStats | Twitch.TwitchLeaderboardStats | HandleLeaderboardChanged | direct |
| Twitch.TwitchLeaderboardStats | Twitch.TwitchLeaderboardStats | HandleLeaderboardChanged | direct |
| Twitch.TwitchLeaderboardStats | Twitch.TwitchLeaderboardStats | HandleStatsChanged | direct |
| Twitch.TwitchLeaderboardStats | Twitch.TwitchLeaderboardStats | HandleStatsChanged | direct |
| Twitch.TwitchLeaderboardStats | Twitch.TwitchLeaderboardStats | HandleStatsChanged | direct |
| Twitch.TwitchLeaderboardStats | Twitch.TwitchLeaderboardStats | HandleStatsChanged | direct |
| Twitch.TwitchLeaderboardStats | Twitch.TwitchLeaderboardStats | HandleStatsChanged | direct |
| Twitch.TwitchLeaderboardStats | Twitch.TwitchLeaderboardStats | HandleStatsChanged | direct |
| Twitch.TwitchLeaderboardStats | Twitch.TwitchLeaderboardStats | HandleStatsChanged | direct |
| Twitch.TwitchLeaderboardStats | Twitch.TwitchLeaderboardStats | HandleStatsChanged | direct |
| Twitch.TwitchLeaderboardStats | Twitch.TwitchLeaderboardStats | HandleStatsChanged | direct |
| Twitch.TwitchLeaderboardStats | Twitch.TwitchLeaderboardStats | HandleStatsChanged | direct |
| Twitch.TwitchLeaderboardStats | Twitch.TwitchLeaderboardStats | HandleStatsChanged | direct |
| Twitch.TwitchManager | Twitch.TwitchManager | CheckCanRespawnEvent | direct |
| Twitch.TwitchManager | Twitch.TwitchManager | CheckCanRespawnEvent | direct |
| Twitch.TwitchManager | Twitch.TwitchManager | HandleSubEvent | direct |
| Twitch.TwitchManager | Twitch.TwitchManager | HandleSubEvent | direct |
| Twitch.TwitchManager | Twitch.TwitchManager | HandleSubEvent | direct |
| Twitch.TwitchManager | Twitch.TwitchManager | IntegrationTypeChanged | direct |
| TwitchActionsFromXml | TwitchActionsFromXml | ParseChannelPointEvent | direct |
| TwitchActionsFromXml | TwitchActionsFromXml | ParseCreatorGoalEvent | direct |
| TwitchActionsFromXml | TwitchActionsFromXml | ParseEvent | direct |
| TwitchActionsFromXml | TwitchActionsFromXml | ParseEvent | direct |
| TwitchActionsFromXml | TwitchActionsFromXml | ParseEvent | direct |
| TwitchActionsFromXml | TwitchActionsFromXml | ParseHypeTrainEvent | direct |
| TwitchActionsFromXml | TwitchActionsFromXml | ParseSubEvent | direct |
| TwitchActionsFromXml | TwitchActionsFromXml | ParseSubEvent | direct |
| UIItemSlot | UIItemSlot | OnDrop | direct |
| Vehicle | Vehicle | FireEvent | direct |
| Voxel | Voxel | OneVoxelStep | direct |
| Voxel | Voxel | OneVoxelStep | direct |
| Voxel | Voxel | OneVoxelStep | direct |
| Voxel | Voxel | OneVoxelStep | direct |
| Weapon | Weapon | OnFireComplete | direct |
| XUiC_CameraWindow | XUiC_CameraWindow | OnClose | direct |
| XUiC_CameraWindow | XUiC_CameraWindow | OnClose | direct |
| XUiC_CategoryList | XUiC_CategoryList | HandleCategoryChanged | direct |
| XUiC_ComboBox | XUiC_ComboBox | PressEvent | direct |
| XUiC_ComboBox | XUiC_ComboBox | TriggerValueChangedEvent | direct |
| XUiC_ComboBox | XUiC_ComboBox | TriggerValueChangedEvent | direct |
| XUiC_ComboBoxFloat | XUiC_ComboBoxFloat | TriggerValueChangedEvent | direct |
| XUiC_ComboBoxFloat | XUiC_ComboBoxFloat | TriggerValueChangedEvent | direct |
| XUiC_ComboBoxInt | XUiC_ComboBoxInt | TriggerValueChangedEvent | direct |
| XUiC_ComboBoxInt | XUiC_ComboBoxInt | TriggerValueChangedEvent | direct |
| XUiC_CompanionEntry | XUiC_CompanionEntry | HasChanged | direct |
| XUiC_Counter | XUiC_Counter | HandleCountChangedEvent | direct |
| XUiC_Counter | XUiC_Counter | HandleCountChangedEvent | direct |
| XUiC_Counter | XUiC_Counter | HandleCountChangedEvent | direct |
| XUiC_Counter | XUiC_Counter | HandleCountChangedEvent | direct |
| XUiC_Counter | XUiC_Counter | HandleCountChangedEvent | direct |
| XUiC_Counter | XUiC_Counter | HandleCountChangedEvent | direct |
| XUiC_Counter | XUiC_Counter | HandleCountChangedEvent | direct |
| XUiC_CreatePoi | XUiC_CreatePoi | depthTextChanged | direct |
| XUiC_CreatePoi | XUiC_CreatePoi | sizeTextChanged | direct |
| XUiC_CreatePoi | XUiC_CreatePoi | sizeTextChanged | direct |
| XUiC_Creative2Stack | XUiC_Creative2Stack | HandleSlotChangeEvent | direct |
| XUiC_DropDown | XUiC_DropDown | OnInputChanged | direct |
| XUiC_DropDown | XUiC_DropDown | SendChangedEvent | direct |
| XUiC_DropDown | XUiC_DropDown | SendSubmitEvent | direct |
| XUiC_HUDStatBar | XUiC_HUDStatBar | hasChanged | direct |
| XUiC_ItemStack | XUiC_ItemStack | HandleSlotChangeEvent | direct |
| XUiC_ItemStack | XUiC_ItemStack | HandleSlotChangeEvent | direct |
| XUiC_ItemStack | XUiC_ItemStack | HandleSlotChangeEvent | direct |
| XUiC_ItemStack | XUiC_ItemStack | HandleSlotChangeEvent | direct |
| XUiC_ItemStack | XUiC_ItemStack | HandleSlotChangeEvent | direct |
| XUiC_ItemStack | XUiC_ItemStack | HandleSlotChangeEvent | direct |
| XUiC_ItemStack | XUiC_ItemStack | HandleSlotChangeEvent | direct |
| XUiC_ItemStack | XUiC_ItemStack | HandleSlotChangeEvent | direct |
| XUiC_ItemStack | XUiC_ItemStack | HandleSlotChangeEvent | direct |
| XUiC_ItemStack | XUiC_ItemStack | HandleSlotChangeEvent | direct |
| XUiC_ItemStack | XUiC_ItemStack | HandleSlotChangeEvent | direct |
| XUiC_ItemStack | XUiC_ItemStack | HandleSlotChangeEvent | direct |
| XUiC_ItemStack | XUiC_ItemStack | HandleSlotChangeEvent | direct |
| XUiC_ItemStack | XUiC_ItemStack | HandleSlotChangeEvent | direct |
| XUiC_ItemStack | XUiC_ItemStack | HandleSlotChangeEvent | direct |
| XUiC_ItemStack | XUiC_ItemStack | HandleSlotChangeEvent | direct |
| XUiC_ItemStack | XUiC_ItemStack | HandleSlotChangeEvent | direct |
| XUiC_ItemStack | XUiC_ItemStack | HandleSlotChangeEvent | direct |
| XUiC_ItemStack | XUiC_ItemStack | HandleSlotChangeEvent | direct |
| XUiC_ItemStack | XUiC_ItemStack | HandleSlotChangeEvent | direct |
| XUiC_ItemStack | XUiC_ItemStack | HandleSlotChangeEvent | direct |
| XUiC_ItemStack | XUiC_ItemStack | HandleSlotChangeEvent | direct |
| XUiC_ItemStack | XUiC_ItemStack | HandleSlotChangeEvent | direct |
| XUiC_ItemStack | XUiC_ItemStack | HandleSlotChangeEvent | direct |
| XUiC_ItemStack | XUiC_ItemStack | HandleSlotChangeEvent | direct |
| XUiC_ItemStack | XUiC_ItemStack | HandleSlotChangeEvent | direct |
| XUiC_List | XUiC_List | OnSearchInputChanged | direct |
| XUiC_List | XUiC_List | OnSearchInputChanged | direct |
| XUiC_List | XUiC_List | OnSearchInputChanged | direct |
| XUiC_List | XUiC_List | OnSelectionChanged | direct |
| XUiC_ListEntry | XUiC_ListEntry | SelectedChanged | direct |
| XUiC_ListEntry | XUiC_ListEntry | SelectedChanged | direct |
| XUiC_MessageBoxWindowGroup | XUiC_MessageBoxWindowGroup | OnOpen | direct |
| XUiC_NewContinueGame | XUiC_NewContinueGame | CbxWorldName_OnValueChanged | direct |
| XUiC_NewContinueGame | XUiC_NewContinueGame | GameModeChanged | direct |
| XUiC_NewContinueGame | XUiC_NewContinueGame | GameModeChanged | direct |
| XUiC_NewContinueGame | XUiC_NewContinueGame | GameModeChanged | direct |
| XUiC_NewContinueGame | XUiC_NewContinueGame | GameModeChanged | direct |
| XUiC_OptionsAudio | XUiC_OptionsAudio | DiscordOnAudioDevicesChanged | direct |
| XUiC_OptionsAudio | XUiC_OptionsAudio | DiscordOnAudioDevicesChanged | direct |
| XUiC_OptionsProfiles | XUiC_OptionsProfiles | OnCloseAction | direct |
| XUiC_OptionsSelector | XUiC_OptionsSelector | HandleSelectionChangedEvent | direct |
| XUiC_OptionsSelector | XUiC_OptionsSelector | HandleSelectionChangedEvent | direct |
| XUiC_OptionsSelector | XUiC_OptionsSelector | HandleSelectionChangedEvent | direct |
| XUiC_PartyEntry | XUiC_PartyEntry | HasChanged | direct |
| XUiC_PlayersList | XUiC_PlayersList | onLocalPlayerChanged | direct |
| XUiC_PopupMenuItem | XUiC_PopupMenuItem | OnHovered | direct |
| XUiC_PrefabFeatureEditorList | XUiC_PrefabFeatureEditorList | OnAddFeaturePressed | direct |
| XUiC_PrefabFeatureEditorList | XUiC_PrefabFeatureEditorList | OnAddFeaturePressed | direct |
| XUiC_QuestOfferWindow | XUiC_QuestOfferWindow | OnCancel | direct |
| XUiC_RecipeCraftCount | XUiC_RecipeCraftCount | HandleCountChangedEvent | direct |
| XUiC_RecipeList | XUiC_RecipeList | OnPressRecipe | direct |
| XUiC_RequiredItemStack | XUiC_RequiredItemStack | HandleSlotChangeEvent | direct |
| XUiC_RequiredItemStack | XUiC_RequiredItemStack | HandleSlotChangeEvent | direct |
| XUiC_SelectableEntry | XUiC_SelectableEntry | SelectedChanged | direct |
| XUiC_ServerBrowserGameOptionInputAdvanced | XUiC_ServerBrowserGameOptionInputAdvanced | OnValueChanged | direct |
| XUiC_ServerBrowserGameOptionInputRange | XUiC_ServerBrowserGameOptionInputRange | OnValueChanged | direct |
| XUiC_ServerBrowserGameOptionInputSimple | XUiC_ServerBrowserGameOptionInputSimple | OnValueChanged | direct |
| XUiC_ServerBrowserGameOptionInputSimple | XUiC_ServerBrowserGameOptionInputSimple | OnValueChanged | direct |
| XUiC_ServerBrowserGamePrefSelector | XUiC_ServerBrowserGamePrefSelector | OnValueChanged | direct |
| XUiC_SkillList | XUiC_SkillList | PagingControl_OnPageChanged | direct |
| XUiC_SkillList | XUiC_SkillList | PagingControl_OnPageChanged | direct |
| XUiC_SliderBar | XUiC_SliderBar | OnScrolled | direct |
| XUiC_SliderBar | XUiC_SliderBar | OnScrolled | direct |
| XUiC_SlotPreview | XUiC_SlotPreview | XUiC_SlotPreview_SlotChangedEvent | direct |
| XUiC_Toolbelt | XUiC_Toolbelt | OnOpen | direct |
| XUiC_Toolbelt | XUiC_Toolbelt | PlayerInventory_OnToolbeltItemsChanged | direct |
| XUiC_VehicleContainer | XUiC_VehicleContainer | OnClose | direct |
| XUiC_WorkstationFuelGrid | XUiC_WorkstationFuelGrid | onFuelItemsChanged | direct |
| XUiC_WorkstationFuelGrid | XUiC_WorkstationFuelGrid | onFuelItemsChanged | direct |
| XUiC_WorkstationMaterialInputWindow | XUiC_WorkstationMaterialInputWindow | onForgeValuesChanged | direct |
| XUiC_WorldGenerationWindowGroup | XUiC_WorldGenerationWindowGroup | TriggerCountyNameChangedEvent | direct |
| XUiC_WorldGenerationWindowGroup | XUiC_WorldGenerationWindowGroup | WorldSizeComboBox_OnValueChanged | direct |
| XUiController | XUiController | InputStyleChanged | direct |
| XUiController | XUiController | InputStyleChanged | direct |
| XUiController | XUiController | OnDoubleClicked | direct |
| XUiController | XUiController | OnDragged | direct |
| XUiController | XUiController | OnHeld | direct |
| XUiController | XUiController | OnHovered | direct |
| XUiController | XUiController | OnPressed | direct |
| XUiController | XUiController | OnScrolled | direct |
| XUiController | XUiController | OnSelected | direct |
| XUiControllerMissing | XUiControllerMissing | OnClose | direct |
| XUiM_PlayerInventory | XUiM_PlayerInventory | onBackpackItemsChanged | direct |
| XUiM_PlayerInventory | XUiM_PlayerInventory | onBackpackItemsChanged | direct |
| XUiM_PlayerInventory | XUiM_PlayerInventory | onBackpackItemsChanged | direct |
| XUiM_PlayerInventory | XUiM_PlayerInventory | onToolbeltItemsChanged | direct |
| XUiM_PlayerInventory | XUiM_PlayerInventory | onToolbeltItemsChanged | direct |
| XUiM_PlayerInventory | XUiM_PlayerInventory | onToolbeltItemsChanged | direct |
| XUiM_PlayerInventory | XUiM_PlayerInventory | onToolbeltItemsChanged | direct |
| XUiV_Button | XUiV_Button | OnHover | direct |
| XUiV_Label | XUiV_Label | OnLanguageSelected | direct |
| XUiView | XUiView | OnHover | direct |
| vp_ItemPickup | vp_ItemPickup | OnFail | direct |
| vp_ItemPickup | vp_ItemPickup | OnSuccess | direct |
| vp_MovingPlatform | vp_MovingPlatform | OnArriveAtDestination | direct |
| vp_MovingPlatform | vp_MovingPlatform | OnArriveAtWaypoint | direct |
| vp_MovingPlatform | vp_MovingPlatform | OnArriveAtWaypoint | direct |
| vp_MovingPlatform | vp_MovingPlatform | OnArriveAtWaypoint | direct |
| vp_MovingPlatform | vp_MovingPlatform | OnStart | direct |
| vp_MovingPlatform | vp_MovingPlatform | OnStop | direct |
