# Living Server port: the files

Source: `chris4aker/OfflineDAoC-LivingServer` at `v1.1.3-beta` (`dd2e856`), paths under `source/server`.
Target: `shadowofze/OfflineDAoC-True165` main at `bfdb8d8`, paths under `development-source/server`.

Written 2026-11-11. See [docs/LIVING-SERVER.md](../../docs/LIVING-SERVER.md) for what the port is.

## Copied unchanged from Living Server (157)

- `GameServer/bots/autonomous/AutonomousBotGroupCoordinator.Chat.cs`
- `GameServer/bots/autonomous/AutonomousBotGuildAlliances.cs`
- `GameServer/bots/autonomous/AutonomousBotGuildChatLines.cs`
- `GameServer/bots/autonomous/AutonomousBotGuildPlanner.cs`
- `GameServer/bots/autonomous/AutonomousBotLinesHeard.cs`
- `GameServer/bots/autonomous/AutonomousBotRageQuits.cs`
- `GameServer/bots/autonomous/AutonomousBotStorylines.cs`
- `GameServer/bots/autonomous/AutonomousBotVoiceCorpus.cs`
- `GameServer/bots/autonomous/AutonomousChatArbiter.cs`
- `GameServer/bots/autonomous/AutonomousChatBroadcast.cs`
- `GameServer/bots/autonomous/AutonomousChatBudget.cs`
- `GameServer/bots/autonomous/AutonomousChatContext.cs`
- `GameServer/bots/autonomous/AutonomousChatDebateSpeech.cs`
- `GameServer/bots/autonomous/AutonomousChatEngagement.cs`
- `GameServer/bots/autonomous/AutonomousChatExchange.cs`
- `GameServer/bots/autonomous/AutonomousChatFieldTalk.cs`
- `GameServer/bots/autonomous/AutonomousChatFiller.cs`
- `GameServer/bots/autonomous/AutonomousChatGear.cs`
- `GameServer/bots/autonomous/AutonomousChatIdiolect.cs`
- `GameServer/bots/autonomous/AutonomousChatMarket.cs`
- `GameServer/bots/autonomous/AutonomousChatNews.cs`
- `GameServer/bots/autonomous/AutonomousChatPlaces.cs`
- `GameServer/bots/autonomous/AutonomousChatQuestions.cs`
- `GameServer/bots/autonomous/AutonomousChatRecall.cs`
- `GameServer/bots/autonomous/AutonomousChatRegulars.cs`
- `GameServer/bots/autonomous/AutonomousChatReplyBudget.cs`
- `GameServer/bots/autonomous/AutonomousChatReplyFit.cs`
- `GameServer/bots/autonomous/AutonomousChatScreen.cs`
- `GameServer/bots/autonomous/AutonomousChatShortTalk.cs`
- `GameServer/bots/autonomous/AutonomousChatStance.cs`
- `GameServer/bots/autonomous/AutonomousChatThreads.cs`
- `GameServer/bots/autonomous/AutonomousChatWhy.cs`
- `GameServer/bots/autonomous/AutonomousHolidays.cs`
- `GameServer/bots/autonomous/AutonomousLivingServer.Memory.cs`
- `GameServer/bots/autonomous/AutonomousLivingServerStore.cs`
- `GameServer/bots/autonomous/AutonomousLivingWorld.cs`
- `GameServer/bots/autonomous/AutonomousPveBossDanger.cs`
- `GameServer/bots/autonomous/AutonomousRepeatDeathSpots.cs`
- `GameServer/bots/autonomous/RealmEventSpoken.cs`
- `GameServer/bots/autonomous/data/chat/ambient_classes_more.lines`
- `GameServer/bots/autonomous/data/chat/ambient_leveling.lines`
- `GameServer/bots/autonomous/data/chat/ambient_life_more.lines`
- `GameServer/bots/autonomous/data/chat/ambient_realms.lines`
- `GameServer/bots/autonomous/data/chat/ambient_realms_balance.lines`
- `GameServer/bots/autonomous/data/chat/ambient_realms_more.lines`
- `GameServer/bots/autonomous/data/chat/beggars.lines`
- `GameServer/bots/autonomous/data/chat/bonds.lines`
- `GameServer/bots/autonomous/data/chat/camp_more.lines`
- `GameServer/bots/autonomous/data/chat/capitals.lines`
- `GameServer/bots/autonomous/data/chat/charity.lines`
- `GameServer/bots/autonomous/data/chat/chat-budget.json`
- `GameServer/bots/autonomous/data/chat/classes.lines`
- `GameServer/bots/autonomous/data/chat/classes_more.lines`
- `GameServer/bots/autonomous/data/chat/closings.lines`
- `GameServer/bots/autonomous/data/chat/combat_calls.lines`
- `GameServer/bots/autonomous/data/chat/debates.lines`
- `GameServer/bots/autonomous/data/chat/debates_general.lines`
- `GameServer/bots/autonomous/data/chat/debates_realms.lines`
- `GameServer/bots/autonomous/data/chat/deeds.lines`
- `GameServer/bots/autonomous/data/chat/dunce.lines`
- `GameServer/bots/autonomous/data/chat/dungeons.lines`
- `GameServer/bots/autonomous/data/chat/experience.lines`
- `GameServer/bots/autonomous/data/chat/extras.lines`
- `GameServer/bots/autonomous/data/chat/followups.lines`
- `GameServer/bots/autonomous/data/chat/gen_alb_threads.lines`
- `GameServer/bots/autonomous/data/chat/gen_hib_threads.lines`
- `GameServer/bots/autonomous/data/chat/gen_mid_threads.lines`
- `GameServer/bots/autonomous/data/chat/gen_rvr_threads.lines`
- `GameServer/bots/autonomous/data/chat/gen_short_scenes.lines`
- `GameServer/bots/autonomous/data/chat/gen_social_threads.lines`
- `GameServer/bots/autonomous/data/chat/group_chatter.lines`
- `GameServer/bots/autonomous/data/chat/group_talk.lines`
- `GameServer/bots/autonomous/data/chat/guild_chat.lines`
- `GameServer/bots/autonomous/data/chat/guild_chat_more.lines`
- `GameServer/bots/autonomous/data/chat/guild_more.lines`
- `GameServer/bots/autonomous/data/chat/holidays.lines`
- `GameServer/bots/autonomous/data/chat/lfg_grouping.lines`
- `GameServer/bots/autonomous/data/chat/lfg_parts.lines`
- `GameServer/bots/autonomous/data/chat/lfg_real.lines`
- `GameServer/bots/autonomous/data/chat/living_server.lines`
- `GameServer/bots/autonomous/data/chat/lore_albion.lines`
- `GameServer/bots/autonomous/data/chat/lore_hibernia.lines`
- `GameServer/bots/autonomous/data/chat/lore_midgard.lines`
- `GameServer/bots/autonomous/data/chat/lore_shared.lines`
- `GameServer/bots/autonomous/data/chat/memory.lines`
- `GameServer/bots/autonomous/data/chat/memory_more.lines`
- `GameServer/bots/autonomous/data/chat/moments.lines`
- `GameServer/bots/autonomous/data/chat/more_realms.lines`
- `GameServer/bots/autonomous/data/chat/news.lines`
- `GameServer/bots/autonomous/data/chat/news_replies.lines`
- `GameServer/bots/autonomous/data/chat/personalities.lines`
- `GameServer/bots/autonomous/data/chat/places.json`
- `GameServer/bots/autonomous/data/chat/replies.lines`
- `GameServer/bots/autonomous/data/chat/replies_113.lines`
- `GameServer/bots/autonomous/data/chat/replies_invites.lines`
- `GameServer/bots/autonomous/data/chat/replies_more.lines`
- `GameServer/bots/autonomous/data/chat/replies_parts.lines`
- `GameServer/bots/autonomous/data/chat/replies_pools.lines`
- `GameServer/bots/autonomous/data/chat/replies_short_chat.lines`
- `GameServer/bots/autonomous/data/chat/roleplayers.lines`
- `GameServer/bots/autonomous/data/chat/rumours.lines`
- `GameServer/bots/autonomous/data/chat/rvr.lines`
- `GameServer/bots/autonomous/data/chat/rvr_more.lines`
- `GameServer/bots/autonomous/data/chat/spammers.lines`
- `GameServer/bots/autonomous/data/chat/storylines.lines`
- `GameServer/bots/autonomous/data/chat/talking_audit.lines`
- `GameServer/bots/autonomous/data/chat/tells.lines`
- `GameServer/bots/autonomous/data/chat/threads_long.lines`
- `GameServer/bots/autonomous/data/chat/trade_crafting.lines`
- `GameServer/bots/autonomous/data/chat/voices.json`
- `GameServer/gameutils/WhisperThrottle.cs`
- `Tests/UnitTests/ChatAuditRoute.cs`
- `Tests/UnitTests/Data/chat_audit_holdout_lines.json`
- `Tests/UnitTests/Data/chat_audit_player_lines.json`
- `Tests/UnitTests/TemporarySqlite.cs`
- `Tests/UnitTests/UT_AutonomousBotGuildAlliances.cs`
- `Tests/UnitTests/UT_AutonomousBotGuildEconomy.cs`
- `Tests/UnitTests/UT_AutonomousBotGuildKeeps.cs`
- `Tests/UnitTests/UT_AutonomousBotGuildSocial.cs`
- `Tests/UnitTests/UT_AutonomousBotGuilds.cs`
- `Tests/UnitTests/UT_AutonomousBotVoices.cs`
- `Tests/UnitTests/UT_AutonomousLivingServer.cs`
- `Tests/UnitTests/UT_Beggars.cs`
- `Tests/UnitTests/UT_ChatAfterRC10.cs`
- `Tests/UnitTests/UT_ChatAfterRC6.cs`
- `Tests/UnitTests/UT_ChatAnswers.cs`
- `Tests/UnitTests/UT_ChatArbiter.cs`
- `Tests/UnitTests/UT_ChatAudit.cs`
- `Tests/UnitTests/UT_ChatBudget.cs`
- `Tests/UnitTests/UT_ChatBudgetSimulation.cs`
- `Tests/UnitTests/UT_ChatBursts.cs`
- `Tests/UnitTests/UT_ChatEngagement.cs`
- `Tests/UnitTests/UT_ChatFiller.cs`
- `Tests/UnitTests/UT_ChatGuildField.cs`
- `Tests/UnitTests/UT_ChatIdiolect.cs`
- `Tests/UnitTests/UT_ChatLogicOct21.cs`
- `Tests/UnitTests/UT_ChatMonth.cs`
- `Tests/UnitTests/UT_ChatRegulars.cs`
- `Tests/UnitTests/UT_ChatReplyAudit.cs`
- `Tests/UnitTests/UT_ChatResponsive.cs`
- `Tests/UnitTests/UT_ChatSecondPass.cs`
- `Tests/UnitTests/UT_ChatSimulation.cs`
- `Tests/UnitTests/UT_ChatSpeakerFit.cs`
- `Tests/UnitTests/UT_ChatThirdPass.cs`
- `Tests/UnitTests/UT_ChatThreads.cs`
- `Tests/UnitTests/UT_ChatWhy.cs`
- `Tests/UnitTests/UT_EffectOrphans.cs`
- `Tests/UnitTests/UT_GroupFollowerStallProbe.cs`
- `Tests/UnitTests/UT_GuildLineRepeats.cs`
- `Tests/UnitTests/UT_LivingServerDay.cs`
- `Tests/UnitTests/UT_LivingServerPacing.cs`
- `Tests/UnitTests/UT_LivingWorld.cs`
- `Tests/UnitTests/UT_PlaytestOct18.cs`
- `Tests/UnitTests/UT_RuntimeAuditOct17.cs`
- `Tests/UnitTests/UT_TalkingAudit.cs`
- `Tests/UnitTests/UT_VoicesSe4.cs`
- `Tests/UnitTests/UT_WhisperThrottle.cs`

## Copied with the mechanical rename only (26)

`IsAutonomousWorldBot` became `IsWorldGamebot`, and `AutonomousBotRegistry.Snapshot()` became `AutonomousBotRegistry.WorldSnapshot()`, so the code never speaks for a hired companion.

- `GameServer/bots/autonomous/AutonomousBeggars.cs`
- `GameServer/bots/autonomous/AutonomousBotBodyLanguage.cs`
- `GameServer/bots/autonomous/AutonomousBotBonds.cs`
- `GameServer/bots/autonomous/AutonomousBotCombatCalls.cs`
- `GameServer/bots/autonomous/AutonomousBotDebates.cs`
- `GameServer/bots/autonomous/AutonomousBotGroupChatter.cs`
- `GameServer/bots/autonomous/AutonomousBotGuildEconomy.cs`
- `GameServer/bots/autonomous/AutonomousBotGuildKeeps.cs`
- `GameServer/bots/autonomous/AutonomousBotGuildSocial.cs`
- `GameServer/bots/autonomous/AutonomousBotPersonality.cs`
- `GameServer/bots/autonomous/AutonomousBotRealmGuilds.cs`
- `GameServer/bots/autonomous/AutonomousBotVoices.cs`
- `GameServer/bots/autonomous/AutonomousChatBursts.cs`
- `GameServer/bots/autonomous/AutonomousChatLiveAnswers.cs`
- `GameServer/bots/autonomous/AutonomousDeathDiagnostics.cs`
- `GameServer/bots/autonomous/AutonomousGoldSpammers.cs`
- `GameServer/bots/autonomous/AutonomousLivingServer.Engage.cs`
- `GameServer/bots/autonomous/AutonomousLivingServer.Events.cs`
- `GameServer/bots/autonomous/AutonomousLivingServer.Tells.cs`
- `GameServer/bots/autonomous/AutonomousLivingServer.cs`
- `GameServer/bots/autonomous/AutonomousPlayerDeeds.cs`
- `GameServer/bots/autonomous/AutonomousRoleplayers.cs`
- `GameServer/bots/autonomous/AutonomousRumours.cs`
- `GameServer/bots/autonomous/BotChatTranscript.cs`
- `GameServer/commands/playercommands/LivingServerWhyCommand.cs`
- `GameServer/commands/playercommands/PersonalityCommand.cs`

## Living Server files edited for True 1.65 (12)

- `GameServer/bots/autonomous/AutonomousBotSchedule.cs`
- `GameServer/bots/autonomous/data/chat/battlegrounds.lines`
- `GameServer/bots/autonomous/data/chat/closings_more.lines`
- `GameServer/bots/autonomous/data/chat/corpus.json`
- `GameServer/commands/gmcommands/LivingServerTestCommand.cs`
- `Tests/UnitTests/UT_AutonomousBotPersonality.cs`
- `Tests/UnitTests/UT_BodyLanguage.cs`
- `Tests/UnitTests/UT_FactionCoherence.cs`
- `Tests/UnitTests/UT_HolidaysAndLeeroy.cs`
- `Tests/UnitTests/UT_LivingServerTestCommand.cs`
- `Tests/UnitTests/UT_TalkingUpdate.cs`
- `Tests/UnitTests/UT_WorldRealism113.cs`

## True 1.65 files changed (34)

Hooks into the talk layer, written by hand against True 1.65's code. `AutonomousBotChatCoordinator.cs`, `AutonomousChatKnowledge.cs`, `AutonomousBotChat.cs`, `AutonomousTownIdleRouting.cs` and the chat commands are Living Server's versions; in the coordinator True 1.65's banter and companion hooks were put back.

- `GameServer/GameServer.csproj`
- `GameServer/bots/BotBrain.cs`
- `GameServer/bots/BotGroupInvite.cs`
- `GameServer/bots/GameBot.cs`
- `GameServer/bots/autonomous/AutonomousBotChat.cs`
- `GameServer/bots/autonomous/AutonomousBotChatCoordinator.cs`
- `GameServer/bots/autonomous/AutonomousBotGroupCoordinator.cs`
- `GameServer/bots/autonomous/AutonomousBotRegistry.cs`
- `GameServer/bots/autonomous/AutonomousChatIntentModel.cs`
- `GameServer/bots/autonomous/AutonomousChatKnowledge.cs`
- `GameServer/bots/autonomous/AutonomousObjectiveAssignments.cs`
- `GameServer/bots/autonomous/AutonomousPopulationController.cs`
- `GameServer/bots/autonomous/AutonomousRealmRaid.cs`
- `GameServer/bots/autonomous/AutonomousRvrEventLayer.cs`
- `GameServer/bots/autonomous/AutonomousTownIdleRouting.cs`
- `GameServer/bots/autonomous/AutonomousWorldBotController.FrontierThreat.cs`
- `GameServer/bots/autonomous/AutonomousWorldBotController.cs`
- `GameServer/bots/autonomous/RealmEventNotices.cs`
- `GameServer/bots/autonomous/population/AutonomousBotLifecycle.cs`
- `GameServer/commands/playercommands/LFG.cs`
- `GameServer/commands/playercommands/broadcast.cs`
- `GameServer/commands/playercommands/emotes.cs`
- `GameServer/commands/playercommands/group.cs`
- `GameServer/commands/playercommands/guildchat.cs`
- `GameServer/commands/playercommands/send.cs`
- `GameServer/commands/playercommands/trade.cs`
- `GameServer/commands/playercommands/yell.cs`
- `GameServer/gameobjects/GameNPC.cs`
- `GameServer/gameutils/Guild.cs`
- `GameServer/keeps/AbstractGameKeep.cs`
- `GameServer/serverproperty/ServerProperties.cs`
- `Tests/Tests.csproj`
- `Tests/UnitTests/UT_AutonomousBotChat.cs`
- `Tests/UnitTests/UT_RealmEventBanter.cs`

## Also added

- `docs/bot-voices/CHAT-BUDGETS.md`
- `docs/bot-voices/DEBATES.md`
- `docs/bot-voices/DESIGN.md`
- `docs/bot-voices/WRITING-GUIDE.md`
- `tools/bot-voices/build_corpus.py`
- `tools/bot-voices/check_runtime_names.py`
- `tools/bot-voices/debates_doc.py`
- `tools/bot-voices/place_levels.py`
- `tools/bot-voices/preview.py`
- `docs/LIVING-SERVER.md`, `tools/living-server-port/PORTED-FILES.md`

## Living Server tests trimmed (7)

- `UT_AutonomousBotPersonality.cs`: the outdoor draw weight test (camp taste is not carried over)
- `UT_BodyLanguage.cs`: the group matchmaking pause tests and the flyer approach test
- `UT_LivingServerTestCommand.cs`: the Leeroy tests
- `UT_HolidaysAndLeeroy.cs`: the Leeroy tests
- `UT_TalkingUpdate.cs`: the settings upgrade tests and the siege outcome news test
- `UT_WorldRealism113.cs`: the frontier transport, camp search and formation budget tests
- `UT_FactionCoherence.cs`: the return line test now allows the one /say line kept for hired companions

## Living Server tests left out (51)

They test parts of Living Server that are not carried over yet (raids and epic encounters, Special Forces, quests, XP rates, routes, diagnostics, bot friends, incoming fire).

`UT_AutonomousRouteGoalFix.cs`, `UT_AutonomousZoneSeamCycle.cs`, `UT_BaseClassStyles.cs`, `UT_BotFriends.cs`, `UT_BotGroupRelease.cs`, `UT_BotIncomingFireReaction.cs`, `UT_BotLogicOct19.cs`, `UT_BotQuietSongs.cs`, `UT_ChatVoiceAfterRC5.cs`, `UT_DarknessFallsExitHold.cs`, `UT_DarknessFallsLoginEntrance.cs`, `UT_DataQuestDeliveryItem.cs`, `UT_DataQuestDependency.cs`, `UT_DepartedReferences.cs`, `UT_DragonRaidWipes.cs`, `UT_DungeonBossGoals.cs`, `UT_EpicChain.cs`, `UT_EpicEncounterTactics.cs`, `UT_EpicSpecialForces.cs`, `UT_EraBotNames.cs`, `UT_ForcedKrondonRaidFixes.cs`, `UT_ForkBuildInfo.cs`, `UT_ForkPicksOct27.cs`, `UT_GroupMatchmaking.cs`, `UT_IndicatorSentStore.cs`, `UT_LevelRangeXpRates.cs`, `UT_LockEventCensus.cs`, `UT_MarfachAndNightSieges.cs`, `UT_MerchantLayout.cs`, `UT_OvernightRunFixes.cs`, `UT_PetOrderTargetGuard.cs`, `UT_PlayTestFixes.cs`, `UT_PrinceDevour.cs`, `UT_QuestExperience.cs`, `UT_QuestIndicators.cs`, `UT_QuestJournal.cs`, `UT_QuestKillCredit.cs`, `UT_QuestLevelWindow.cs`, `UT_RaidDungeonRoutes.cs`, `UT_RealmRaidWinBalance.cs`, `UT_RepairSkillGate.cs`, `UT_ScurceolOrbStandProbe.cs`, `UT_ScurceolOrbStands.cs`, `UT_ServerHolderCensus.cs`, `UT_SiegeBalanceNov06.cs`, `UT_SiegeDefenseAtMarch.cs`, `UT_SpecialForcesJoinFullRaids.cs`, `UT_SqlitePooledReadContention.cs`, `UT_WaterBreathingEffect.cs`, `UT_WorldDatabaseSafety.cs`, `UT_XanxicarStompFix.cs`
