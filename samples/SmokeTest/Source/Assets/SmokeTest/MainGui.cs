﻿// <copyright file="MainGui.cs" company="Google Inc.">
// Copyright (C) 2014 Google Inc.
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//  http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//    limitations under the License.
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using GooglePlayGames;
using GooglePlayGames.BasicApi;
using GooglePlayGames.BasicApi.Multiplayer;
using GooglePlayGames.BasicApi.Quests;
using GooglePlayGames.BasicApi.SavedGame;
using GooglePlayGames.OurUtils;
using UnityEngine;

public class MainGui : MonoBehaviour, OnStateLoadedListener,
RealTimeMultiplayerListener
{
    public GUISkin GuiSkin;

    private const int Spacing = 4;
    private const int Margin = 8;
    private const float FontSizeFactor = 35;
    private const int GridCols = 2;
    private const int GridRows = 10;

    private static readonly PlayGamesClientConfiguration ClientConfiguration =
        new PlayGamesClientConfiguration.Builder()
            .EnableSavedGames()
            .EnableDeprecatedCloudSave()
            .Build();

    private Ui mUi = Ui.Main;

    private bool mStandby = false;
    private string mStandbyMessage = string.Empty;
    private string _mStatus = "Ready";
    private string mLastInvitationId = null;

    private string mSavedGameFilename = "default_name";
    private ISavedGameMetadata mCurrentSavedGame = null;
    private string mSavedGameFileContent = string.Empty;
    private IConflictResolver mConflictResolver = null;
    private ISavedGameMetadata mConflictOriginal = null;
    private string mConflictOriginalData = null;
    private ISavedGameMetadata mConflictUnmerged = null;
    private string mConflictUnmergedData = null;
    private string mLastLocalSave = null;

    private string mConflictLocalVersion = null;
    private string mConflictServerVersion = null;
    private bool mHadCloudConflict = false;

    private volatile TurnBasedMatch mMatch = null;
    private volatile IQuest mQuest = null;
    private volatile IQuestMilestone mQuestMilestone = null;

    private NearbyGUI mNearbyGui;

    // which UI are we showing?
    public enum Ui
    {
        Main,
        Multiplayer,
        Rtmp,
        SavedGame,
        EditSavedGameName,
        WriteSavedGame,
        ResolveSaveConflict,
        Tbmp,
        TbmpMatch,
        QuestsAndEvents,
        NearbyConnections
    }

    public void Start()
    {
        Screen.orientation = ScreenOrientation.Portrait;

        PlayGamesPlatform.DebugLogEnabled = true;
        this.mNearbyGui = new NearbyGUI(this);
    }

    public void SetUI(Ui page)
    {
        this.mUi = page;
    }

    public Rect CalcGrid(int col, int row)
    {
        return this.CalcGrid(col, row, 1, 1);
    }

    Rect CalcGrid(int col, int row, int colcount, int rowcount)
    {
        int cellW = (Screen.width - 2 * Margin - (GridCols - 1) * Spacing) / GridCols;
        int cellH = (Screen.height - 2 * Margin - (GridRows - 1) * Spacing) / GridRows;
        return new Rect(Margin + col * (cellW + Spacing),
            Margin + row * (cellH + Spacing),
            cellW + (colcount - 1) * (Spacing + cellW),
            cellH + (rowcount - 1) * (Spacing + cellH));
    }

    public byte[] OnStateConflict(int slot, byte[] local, byte[] server)
    {
        mHadCloudConflict = true;

        mConflictLocalVersion = System.Text.ASCIIEncoding.Default.GetString(local);
        mConflictServerVersion = System.Text.ASCIIEncoding.Default.GetString(server);

        Logger.d(string.Format("Found conflict! local:{0}, server:{1}", mConflictLocalVersion, mConflictServerVersion));
        return local;
    }

    public void OnStateSaved(bool success, int slot)
    {
        Status = "Cloud save " + (success ? "successful" : "failed") + " word: " + mLastLocalSave;
        ShowEffect(success);
    }

    public void OnRealTimeMessageReceived(bool reliable, string senderId, byte[] data)
    {
        Status = string.Format(
            "Got message. Reliable:{0} From:{1} Data: {2}",
            reliable,
            senderId,
            System.Text.ASCIIEncoding.Default.GetString(data));
    }

    public void OnRoomSetupProgress(float progress)
    {
        SetStandBy("Setting up room (" + ((int)progress) + "%)");
    }

    public void OnRoomConnected(bool success)
    {
        ShowEffect(success);
        Status = success ? "Room connected!" : "Room setup failed!";
        EndStandBy();
    }

    public void OnLeftRoom()
    {
        Status = "Left room.";
    }

    public void OnPeersConnected(string[] participantIds)
    {
        Status = "Peers connected: ";
        foreach (string pid in participantIds)
        {
            Participant p = PlayGamesPlatform.Instance.RealTime.GetParticipant(pid);
            if (p != null)
            {
                Status += pid + "(" + p.DisplayName + ") ";
            }
            else
            {
                Status += pid + "(NULL) ";
            }
        }
    }

    public void OnPeersDisconnected(string[] participantIds)
    {
        Status = "Peers disconnected: ";
        foreach (string pid in participantIds)
        {
            Participant p = PlayGamesPlatform.Instance.RealTime.GetParticipant(pid);
            if (p != null)
            {
                Status += pid + "(" + p.DisplayName + ") ";
            }
            else
            {
                Status += pid + "(NULL) ";
            }
        }
    }

    public void OnStateLoaded(bool success, int slot, byte[] data)
    {
        EndStandBy();
        if (success)
        {
            Status = "Loaded from cloud: " + System.Text.ASCIIEncoding.Default.GetString(data);
        }
        else
        {
            Status = "*** Failed to load from cloud.";
        }

        Status += ". conflict=" + (mHadCloudConflict ? "yes" : "no");

        if (mHadCloudConflict)
        {
            Status += string.Format(" local={0}, server={1}", mConflictLocalVersion, mConflictServerVersion);
        }

        ShowEffect(success);
    }

    internal void ShowStandbyUi()
    {
        GUI.Label(this.CalcGrid(0, 2, 2, 1), this.mStandbyMessage);
    }

    internal void ShowNotAuthUi()
    {
        this.DrawTitle(null);
        this.DrawStatus();
        if (GUI.Button(this.CalcGrid(0, 1), "Authenticate"))
        {
            this.DoAuthenticate();
        }
      
        if (GUI.Button(this.CalcGrid(1, 1), "Nearby Connections"))
        {
            this.mUi = Ui.NearbyConnections;
        }
    }

    internal void ShowRegularUi()
    {
        this.DrawTitle(null);
        this.DrawStatus();

        if (GUI.Button(this.CalcGrid(0, 1), "Ach Reveal"))
        {
            this.DoAchievementReveal();
        }
        else if (GUI.Button(this.CalcGrid(0, 2), "Ach Unlock"))
        {
            this.DoAchievementUnlock();
        }
        else if (GUI.Button(this.CalcGrid(0, 3), "Ach Increment"))
        {
            this.DoAchievementIncrement();
        }
        else if (GUI.Button(this.CalcGrid(0, 4), "Ach Show UI"))
        {
            this.DoAchievementUI();
        }

        if (GUI.Button(this.CalcGrid(1, 1), "Post Score"))
        {
            this.DoPostScore();
        }
        else if (GUI.Button(this.CalcGrid(1, 2), "LB Show UI"))
        {
            this.DoLeaderboardUI();
        }
        else if (GUI.Button(this.CalcGrid(1, 3), "Cloud Save"))
        {
            this.DoCloudSave();
        }
        else if (GUI.Button(this.CalcGrid(1, 4), "Cloud Load"))
        {
            this.DoCloudLoad();
        }

        if (GUI.Button(this.CalcGrid(0, 5), "Multiplayer"))
        {
            this.mUi = Ui.Multiplayer;
        }

        if (GUI.Button(this.CalcGrid(1, 5), "Quests / Events"))
        {
            this.mUi = Ui.QuestsAndEvents;
        }

        if (GUI.Button(this.CalcGrid(0, 6), "Saved Game"))
        {
            this.mUi = Ui.SavedGame;
        }

        if (GUI.Button(this.CalcGrid(1, 6), "Nearby Connections"))
        {
            this.mUi = Ui.NearbyConnections;
        }

        if (GUI.Button(this.CalcGrid(0, 7), "Sign Out"))
        {
            this.DoSignOut();
        }
    }

    internal void ShowMultiplayerUi()
    {
        this.DrawTitle("MULTIPLAYER");
        this.DrawStatus();

        if (GUI.Button(this.CalcGrid(0, 1), "RTMP"))
        {
            this.mUi = Ui.Rtmp;
        }
        else if (GUI.Button(this.CalcGrid(1, 1), "TBMP"))
        {
            this.mUi = Ui.Tbmp;
        }
        else if (GUI.Button(this.CalcGrid(1, 5), "Back"))
        {
            this.mUi = Ui.Main;
        }
    }

    internal void ShowEditSavedGameName()
    {
        this.DrawTitle("EDIT SAVED GAME FILENAME");
        this.DrawStatus();

        this.mSavedGameFilename = GUI.TextArea(this.CalcGrid(0, 1), this.mSavedGameFilename);

        if (GUI.Button(this.CalcGrid(1, 7), "Back"))
        {
            this.mUi = Ui.SavedGame;
        }
    }

    internal void ShowResolveConflict()
    {
        this.DrawTitle("RESOLVE SAVE GAME CONFLICT");
        this.DrawStatus();

        if (this.mConflictResolver == null)
        {
            Status = "No pending conflict";
            mUi = Ui.SavedGame;
            return;
        }

        string msg = "Original: " + mConflictOriginal.Filename + ":" + mConflictOriginal.Description + "\n" +
                     "Data: " + mConflictOriginalData;
        GUI.Label(CalcGrid(0, 1, 2, 2), msg);

        msg = "Unmerged: " + mConflictUnmerged.Filename + ":" + mConflictUnmerged.Description + "\n" +
        "Data: " + mConflictUnmergedData;
        GUI.Label(CalcGrid(0, 2, 2, 2), msg);

        if (GUI.Button(CalcGrid(0, 3), "Use Original"))
        {
            mConflictResolver.ChooseMetadata(mConflictOriginal);
            SetStandBy("Choosing original, retrying open");
            mUi = Ui.SavedGame;
        }
        else if (GUI.Button(CalcGrid(1, 3), "Use Unmerged"))
        {
            mConflictResolver.ChooseMetadata(mConflictUnmerged);
            SetStandBy("Choosing unmerged, retrying open");
            mUi = Ui.SavedGame;
        }

        if (GUI.Button(CalcGrid(1, 7), "Back"))
        {
            mUi = Ui.SavedGame;
        }
    }

    internal void ShowWriteSavedGame()
    {
        DrawTitle("WRITE SAVED GAME");
        DrawStatus();

        mSavedGameFileContent = GUI.TextArea(CalcGrid(0, 1), mSavedGameFileContent);

        if (mCurrentSavedGame == null || !mCurrentSavedGame.IsOpen)
        {
            Status = "No opened saved game selected.";
            mUi = Ui.SavedGame;
            return;
        }

        var update = new SavedGameMetadataUpdate.Builder()
            .WithUpdatedDescription("Saved at " + DateTime.Now.ToString())
            .WithUpdatedPlayedTime(mCurrentSavedGame.TotalTimePlayed.Add(TimeSpan.FromHours(1)))
            .Build();

        if (GUI.Button(CalcGrid(0, 7), "Write"))
        {
            SetStandBy("Writing update");
            PlayGamesPlatform.Instance.SavedGame.CommitUpdate(
                mCurrentSavedGame,
                update,
                System.Text.ASCIIEncoding.Default.GetBytes(mSavedGameFileContent),
                (status, updated) =>
                {
                    Status = "Write status was: " + status;
                    mUi = Ui.SavedGame;
                    EndStandBy();
                });
            mCurrentSavedGame = null;
        }
        else if (GUI.Button(CalcGrid(1, 7), "Cancel"))
        {
            mUi = Ui.SavedGame;
        }
    }

    internal void OpenSavedGame(ConflictResolutionStrategy strategy)
    {
        SetStandBy("Opening using strategy: " + strategy);
        PlayGamesPlatform.Instance.SavedGame.OpenWithAutomaticConflictResolution(
            mSavedGameFilename,
            DataSource.ReadNetworkOnly,
            strategy,
            (status, openedFile) =>
            {
                Status = "Open status for file " + mSavedGameFilename + ": " + status + "\n";
                if (openedFile != null)
                {
                    Status += "Successfully opened file: " + openedFile.ToString();
                    Logger.d("Opened file: " + openedFile.ToString());
                    mCurrentSavedGame = openedFile;
                }
          
                EndStandBy();
            });
    }

    internal void DoReadSavedGame()
    {
        if (mCurrentSavedGame == null)
        {
            ShowEffect(false);
            Status = "No save game selected";
            return;
        }

        if (!mCurrentSavedGame.IsOpen)
        {
            ShowEffect(false);
            Status = "Current saved game is not open. Open it first.";
            return;
        }

        SetStandBy("Reading file: " + mSavedGameFilename);
        var openedFile = mSavedGameFilename;
        PlayGamesPlatform.Instance.SavedGame.ReadBinaryData(
            mCurrentSavedGame,
            (status, binaryData) =>
            {
                Status = "Reading file " + openedFile + ", status: " + status + "\n";

                if (binaryData != null)
                {
                    var stringContent = System.Text.ASCIIEncoding.Default.GetString(binaryData);
                    Status += "File content: " + stringContent;
                    mSavedGameFileContent = stringContent;
                }
                else
                {
                    mSavedGameFileContent = string.Empty;
                }
          
                EndStandBy();
            });
    }

    internal void DoShowSavedGameUI()
    {
        SetStandBy("Showing saved game UI");
        PlayGamesPlatform.Instance.SavedGame.ShowSelectSavedGameUI(
            "Saved Game UI",
            10,
            false,
            false,
            (status, savedGame) =>
            {
                Status = "UI Status: " + status;
                if (savedGame != null)
                {
                    Status +=
                        "Retrieved saved game with description: " + savedGame.Description;
                    mCurrentSavedGame = savedGame;
                }
          
                EndStandBy();
            });
    }

    internal void DoOpenManual()
    {
        SetStandBy("Manual opening file: " + mSavedGameFilename);
        PlayGamesPlatform.Instance.SavedGame.OpenWithManualConflictResolution(
            mSavedGameFilename,
            DataSource.ReadNetworkOnly,
            true,
            (resolver, original, originalData, unmerged, unmergedData) =>
            {
                Logger.d("Entering conflict callback");
                mConflictResolver = resolver;
                mConflictOriginal = original;
                mConflictOriginalData = System.Text.ASCIIEncoding.Default.GetString(originalData);
                mConflictUnmerged = unmerged;
                mConflictUnmergedData = System.Text.ASCIIEncoding.Default.GetString(unmergedData);
                mUi = Ui.ResolveSaveConflict;
                EndStandBy();
                Logger.d("Encountered manual open conflict.");
            },
            (status, openedFile) =>
            {
                Status = "Open status for file " + mSavedGameFilename + ": " + status + "\n";
                if (openedFile != null)
                {
                    Status += "Successfully opened file: " + openedFile.ToString();
                    Logger.d("Opened file: " + openedFile.ToString());
                    mCurrentSavedGame = openedFile;
                }
          
                EndStandBy();
            });
    }

    internal void DoFetchAll()
    {
        SetStandBy("Fetching All Saved Games");
        PlayGamesPlatform.Instance.SavedGame.FetchAllSavedGames(
            DataSource.ReadNetworkOnly,
            (status, savedGames) =>
            {
                Status = "Fetch All Status: " + status + "\n";
                Status += "Saved Games: [" +
                string.Join(",", savedGames.Select(g => g.Filename).ToArray()) + "]";
                savedGames.ForEach(g => Logger.d("Retrieved save game: " + g.ToString()));
                EndStandBy();
            });
    }

    internal void ShowSavedGameUi()
    {
        DrawTitle("SAVED GAME - Using file: " + mSavedGameFilename);
        DrawStatus();

        if (GUI.Button(CalcGrid(0, 1), "Show UI"))
        {
            DoShowSavedGameUI();
        }
        else if (GUI.Button(CalcGrid(1, 1), "Open Manual"))
        {
            DoOpenManual();
        }
        else if (GUI.Button(CalcGrid(0, 2), "Open Keep Original"))
        {
            OpenSavedGame(ConflictResolutionStrategy.UseOriginal);
        }
        else if (GUI.Button(CalcGrid(1, 2), "Open Keep Unmerged"))
        {
            OpenSavedGame(ConflictResolutionStrategy.UseUnmerged);
        }
        else if (GUI.Button(CalcGrid(0, 3), "Read"))
        {
            DoReadSavedGame();
        }
        else if (GUI.Button(CalcGrid(1, 3), "Write"))
        {
            mUi = Ui.WriteSavedGame;
        }
        else if (GUI.Button(CalcGrid(0, 4), "Fetch All"))
        {
            DoFetchAll();
        }
        else if (GUI.Button(CalcGrid(1, 4), "Edit Filename"))
        {
            mUi = Ui.EditSavedGameName;
        }
        else if (GUI.Button(CalcGrid(1, 6), "Back"))
        {
            mUi = Ui.Main;
        }
    }

    internal void ShowRtmpUi()
    {
        DrawTitle("REAL-TIME MULTIPLAYER");
        DrawStatus();

        if (GUI.Button(CalcGrid(0, 1), "Quick Game 2p"))
        {
            DoQuickGame(2);
        }
        else if (GUI.Button(CalcGrid(0, 2), "Create Game"))
        {
            DoCreateGame();
        }
        else if (GUI.Button(CalcGrid(0, 3), "From Inbox"))
        {
            DoAcceptFromInbox();
        }
        else if (GUI.Button(CalcGrid(1, 1), "Broadcast msg"))
        {
            DoBroadcastMessage();
        }
        else if (GUI.Button(CalcGrid(0, 4), "Send msg"))
        {
            DoSendMessage();
        }
        else if (GUI.Button(CalcGrid(1, 2), "Who Is Here"))
        {
            DoListParticipants();
        }
        else if (GUI.Button(CalcGrid(1, 3), "Accept incoming"))
        {
            DoAcceptIncoming();
        }
        else if (GUI.Button(CalcGrid(1, 4), "Decline incoming"))
        {
            DoDeclineIncoming();
        }
        else if (GUI.Button(CalcGrid(0, 5), "Leave Room"))
        {
            DoLeaveRoom();
        }
        else if (GUI.Button(CalcGrid(1, 5), "Back"))
        {
            mUi = Ui.Multiplayer;
        }
    }

    internal void ShowTbmpUi()
    {
        DrawTitle("TURN-BASED MULTIPLAYER");
        DrawStatus();

        if (GUI.Button(CalcGrid(0, 1), "Quick Game 2p"))
        {
            DoTbmpQuickGame();
        }
        else if (GUI.Button(CalcGrid(0, 2), "Create Game"))
        {
            DoTbmpCreateGame();
        }
        else if (GUI.Button(CalcGrid(0, 3), "View all Matches"))
        {
            DoTbmpAcceptFromInbox();
        }
        else if (GUI.Button(CalcGrid(1, 1), "Accept incoming"))
        {
            DoTbmpAcceptIncoming();
        }
        else if (GUI.Button(CalcGrid(1, 2), "Decline incoming"))
        {
            DoTbmpDeclineIncoming();
        }
        else if (GUI.Button(CalcGrid(1, 3), "Match..."))
        {
            if (mMatch == null)
            {
                Status = "No match active.";
            }
            else
            {
                mUi = Ui.TbmpMatch;
            }
        }
        else if (GUI.Button(CalcGrid(1, 5), "Back"))
        {
            mUi = Ui.Multiplayer;
        }
    }

    internal void ShowTbmpMatchUi()
    {
        DrawTitle("TURN-BASED MULTIPLAYER MATCH\n" + GetMatchSummary());
        DrawStatus();

        if (GUI.Button(CalcGrid(0, 1), "Match Data"))
        {
            DoTbmpShowMatchData();
        }
        else if (GUI.Button(CalcGrid(0, 2), "Take Turn"))
        {
            DoTbmpTakeTurn();
        }
        else if (GUI.Button(CalcGrid(0, 3), "Finish"))
        {
            DoTbmpFinish();
        }
        else if (GUI.Button(CalcGrid(0, 4), "Ack Finish"))
        {
            DoTbmpAckFinish();
        }
        else if (GUI.Button(CalcGrid(0, 5), "Max Data Size"))
        {
            Status = PlayGamesPlatform.Instance.TurnBased.GetMaxMatchDataSize() + " bytes";
        }
        else if (GUI.Button(CalcGrid(1, 1), "Leave"))
        {
            DoTbmpLeave();
        }
        else if (GUI.Button(CalcGrid(1, 2), "Leave During Turn"))
        {
            DoTbmpLeaveDuringTurn();
        }
        else if (GUI.Button(CalcGrid(1, 3), "Cancel"))
        {
            DoTbmpCancel();
        }
        else if (GUI.Button(CalcGrid(1, 4), "Rematch"))
        {
            DoTbmpRematch();
        }
        else if (GUI.Button(CalcGrid(1, 5), "Back"))
        {
            mUi = Ui.Tbmp;
        }
    }

    internal void ShowQuestsAndEventsUi()
    {
        if (mQuest != null)
        {
            Status = "Selected Quest: " + mQuest.Id + "\n";
        }

        if (mQuestMilestone != null)
        {
            Status += "Selected Milestone: " + mQuestMilestone.Id;
        }

        DrawStatus();
        DrawTitle("Quests and Events");

        if (GUI.Button(CalcGrid(0, 1), "Fetch All Events"))
        {
            SetStandBy("Fetching All Events");
            PlayGamesPlatform.Instance.Events.FetchAllEvents(
                DataSource.ReadNetworkOnly,
                (status, events) =>
                {
                    Status = "Fetch All Status: " + status + "\n";
                    Status += "Events: [" +
                    string.Join(",", events.Select(g => g.Id).ToArray()) + "]";
                    events.ForEach(e => Logger.d("Retrieved event: " + e.ToString()));
                    EndStandBy();
                });
        }
        else if (GUI.Button(CalcGrid(1, 1), "Fetch Event"))
        {
            SetStandBy("Fetching Event");
            PlayGamesPlatform.Instance.Events.FetchEvent(
                DataSource.ReadNetworkOnly,
                Settings.Event,
                (status, fetchedEvent) =>
                {
                    Status = "Fetch Status: " + status + "\n";
                    if (fetchedEvent != null)
                    {
                        Status += "Event: [" + fetchedEvent.Id + ", " + fetchedEvent.Description + "]";
                        Logger.d("Fetched event: " + fetchedEvent.ToString());
                    }
              
                    EndStandBy();
                });
        }
        else if (GUI.Button(CalcGrid(0, 2), "Increment Event"))
        {
            PlayGamesPlatform.Instance.Events.IncrementEvent(Settings.Event, 10);
        }

        if (GUI.Button(CalcGrid(1, 2), "Fetch Open Quests"))
        {
            FetchQuestList(QuestFetchFlags.Open);
        }
        else if (GUI.Button(CalcGrid(0, 3), "Fetch Upcoming Quests"))
        {
            FetchQuestList(QuestFetchFlags.Upcoming);
        }
        else if (GUI.Button(CalcGrid(1, 3), "Fetch Accepted Quests"))
        {
            FetchQuestList(QuestFetchFlags.Accepted);
        }
        else if (GUI.Button(CalcGrid(0, 4), "Show All Quests UI"))
        {
            SetStandBy("Showing all Quest UI");
            mQuest = null;
            mQuestMilestone = null;
            PlayGamesPlatform.Instance.Quests.ShowAllQuestsUI(HandleQuestUI);
        }
        else if (GUI.Button(CalcGrid(1, 4), "Show Quest UI"))
        {
            if (mQuest == null)
            {
                Status = "Could not show Quest UI - no quest selected";
            }
            else
            {
                PlayGamesPlatform.Instance.Quests.ShowSpecificQuestUI(mQuest, HandleQuestUI);
            }
        }
        else if (GUI.Button(CalcGrid(0, 5), "Fetch Quest"))
        {
            if (mQuest == null)
            {
                Status = "Could not fetch Quest - no quest selected";
            }
            else
            {
                SetStandBy("Fetching Quest");
                PlayGamesPlatform.Instance.Quests.Fetch(
                    DataSource.ReadNetworkOnly,
                    mQuest.Id,
                    (status, quest) =>
                    {
                        Status = "Fetch Quest Status: " + status + "\n";
                        mQuest = quest;
                        Logger.d("Fetched quest " + quest);
                        EndStandBy();
                    });
            }
        }
        else if (GUI.Button(CalcGrid(1, 5), "Accept Quest"))
        {
            if (mQuest == null)
            {
                Status = "Could not accept Quest - no quest selected";
            }
            else
            {
                SetStandBy("Accepting quest");
                PlayGamesPlatform.Instance.Quests.Accept(
                    mQuest,
                    (status, quest) =>
                    {
                        Status = "Accept Quest Status: " + status + "\n";
                        mQuest = quest;
                        Logger.d("Accepted quest " + quest);
                        EndStandBy();
                    });
            }
        }
        else if (GUI.Button(CalcGrid(0, 6), "Claim Milestone"))
        {
            if (mQuestMilestone == null)
            {
                Status = "Could not claim milestone - no milestone selected";
            }
            else
            {
                SetStandBy("Claiming milestone");
                PlayGamesPlatform.Instance.Quests.ClaimMilestone(
                    mQuestMilestone,
                    (status, quest, milestone) =>
                    {
                        Status = "Claim milestone Status: " + status + "\n";
                        mQuest = quest;
                        mQuestMilestone = milestone;
                        Logger.d("Claim quest: " + quest);
                        Logger.d("Claim milestone: " + milestone);
                        EndStandBy();
                    });
            }
        }

        if (GUI.Button(CalcGrid(1, 6), "Back"))
        {
            mUi = Ui.Main;
        }
    }

    internal void DrawTitle(string title)
    {
        GUI.Label(
            this.CalcGrid(0, 0, 2, 1), 
            title == null ? "Play Games Unity Plugin - Smoke Test" : title);
    }

    internal string Status
    {
        get
        {
            return _mStatus;
        }
        set
        {
            _mStatus = value;
        }
    }

    internal void DrawStatus()
    {
        GUI.Label(this.CalcGrid(0, 8, 2, 2), this.Status);
    }

    internal void FetchQuestList(QuestFetchFlags flags)
    {
        SetStandBy("Fetching Quests, flags: " + flags);
        PlayGamesPlatform.Instance.Quests.FetchMatchingState(
            DataSource.ReadNetworkOnly,
            flags,
            (status, quests) =>
            {
                string statusText;
                statusText = "Fetch Status: " + status + "\n";
                statusText += "Quests: [" +
                string.Join(",", quests.Select(g => g.Id).ToArray()) + "]";

                Status = statusText;

                if (quests.Count != 0)
                {
                    mQuest = quests[0];
                }

                quests.ForEach(q => Logger.d("Retrieved quest: " + q.ToString()));
                EndStandBy();
            });
    }

    internal void HandleQuestUI(QuestUiResult result, IQuest quest, IQuestMilestone milestone)
    {
        Status = "Show UI Status: " + Status + "\n";
        Logger.d("UI Status: " + result);
        if (quest != null)
        {
            mQuest = quest;
            Status += "User wanted to accept quest " + quest.Id;
            Logger.d("User Accepted quest " + quest.ToString());
        }
        else if (milestone != null)
        {
            mQuestMilestone = milestone;
            Status += "User wanted to claim milestone " + milestone.Id;
            Logger.d("Claimed milestone " + milestone.ToString());
            Logger.d("Completion data: " +
                System.Text.ASCIIEncoding.Default.GetString(milestone.CompletionRewardData));
        }

        EndStandBy();
    }

    internal void ShowEffect(bool success)
    {
        Camera.main.backgroundColor = success ?
            new Color(0.0f, 0.0f, 0.8f, 1.0f) :
            new Color(0.8f, 0.0f, 0.0f, 1.0f);
    }

    internal int CalcFontSize()
    {
        return (int)(Screen.width * FontSizeFactor / 1000.0f);
    }

    // Update is called once per frame
    internal void OnGUI()
    {
        GUI.skin = GuiSkin;
        GUI.skin.label.fontSize = CalcFontSize();
        GUI.skin.button.fontSize = CalcFontSize();
        GUI.skin.textArea.fontSize = CalcFontSize();

        if (mStandby)
        {
            ShowStandbyUi();
        }
        else if (mUi == Ui.NearbyConnections)
        {
            mNearbyGui.OnGUI();
        }
        else if (Social.localUser.authenticated)
        {
            switch (mUi)
            {
                case Ui.Rtmp:
                    ShowRtmpUi();
                    break;
                case Ui.Multiplayer:
                    ShowMultiplayerUi();
                    break;
                case Ui.EditSavedGameName:
                    ShowEditSavedGameName();
                    break;
                case Ui.WriteSavedGame:
                    ShowWriteSavedGame();
                    break;
                case Ui.SavedGame:
                    ShowSavedGameUi();
                    break;
                case Ui.ResolveSaveConflict:
                    ShowResolveConflict();
                    break;
                case Ui.Tbmp:
                    ShowTbmpUi();
                    break;
                case Ui.TbmpMatch:
                    ShowTbmpMatchUi();
                    break;
                case Ui.QuestsAndEvents:
                    ShowQuestsAndEventsUi();
                    break;
                case Ui.NearbyConnections:
                    mNearbyGui.OnGUI();
                    break;
                default:
                    // check for a status of interest, and if there
                    // is one, then don't touch it.  Otherwise
                    // show the logged in user.
                    if (string.IsNullOrEmpty(Status) || Status == "Ready")
                    {
                        Status = "Authenticated. Hello, " +
                            Social.localUser.userName + " (" +
                            Social.localUser.id + ")";
                    }
                        ShowRegularUi();
                    break;
            }
        }
        else
        {
            ShowNotAuthUi();
        }
    }

    internal void SetStandBy(string message)
    {
        mStandby = true;
        mStandbyMessage = message;
    }

    internal void EndStandBy()
    {
        mStandby = false;
    }

    internal void DoAuthenticate()
    {
        SetStandBy("Authenticating...");

        PlayGamesPlatform.InitializeInstance(ClientConfiguration);
        PlayGamesPlatform.Activate();
        Social.localUser.Authenticate((bool success) =>
            {
                EndStandBy();
                if (success)
                {
                    Status = "Authenticated. Hello, " + Social.localUser.userName + " (" +
                    Social.localUser.id + ")";

                    // register delegates
                    PlayGamesPlatform.Instance.RegisterInvitationDelegate(OnInvitationReceived);
                    if (PlayGamesPlatform.Instance.TurnBased != null)
                    {
                        PlayGamesPlatform.Instance.TurnBased.RegisterMatchDelegate(
                            OnMatchFromNotification);
                    }
                }
                else
                {
                    Status = "*** Failed to authenticate.";
                }
          
                ShowEffect(success);
            });
    }

    internal void DoSignOut()
    {
        ((PlayGamesPlatform)Social.Active).SignOut();
        Status = "Signing out.";
    }

    internal void DoAchievementReveal()
    {
        SetStandBy("Revealing achievement...");
        Social.ReportProgress(
            Settings.AchievementToReveal,
            0.0f,
            (bool success) =>
            {
                EndStandBy();
                Status = success ? "Revealed successfully." : "*** Failed to reveal ach.";
                Debug.Log("AchievementToReveal completed: " + Status);
                ShowEffect(success);
            });
    }

    internal void DoAchievementUnlock()
    {
        SetStandBy("Unlocking achievement...");
        Social.ReportProgress(
            Settings.AchievementToUnlock,
            100.0f,
            (bool success) =>
            {
                EndStandBy();
                Status = success ? "Unlocked successfully." : "*** Failed to unlock ach.";
                ShowEffect(success);
            });
    }

    internal void DoAchievementIncrement()
    {
        PlayGamesPlatform p = (PlayGamesPlatform)Social.Active;

        SetStandBy("Incrementing achievement...");
        p.IncrementAchievement(
            Settings.AchievementToIncrement,
            1,
            (bool success) =>
            {
                EndStandBy();
                Status = success ? "Incremented successfully." : "*** Failed to increment ach.";
                ShowEffect(success);
            });
    }

    internal long GenScore()
    {
        return (long)DateTime.Today.Subtract(new DateTime(2013, 1, 1, 0, 0, 0)).TotalSeconds;
    }

    internal void DoPostScore()
    {
        long score = GenScore();
        SetStandBy("Posting score: " + score);
        Social.ReportScore(
            score,
            Settings.Leaderboard,
            (bool success) =>
            {
                EndStandBy();
                Status = success ? "Successfully reported score " + score :
                "*** Failed to report score " + score;
                ShowEffect(success);
            });
    }

    internal void DoLeaderboardUI()
    {
        Social.ShowLeaderboardUI();
        ShowEffect(true);
    }

    internal void DoAchievementUI()
    {
        Social.ShowAchievementsUI();
        ShowEffect(true);
    }

    internal char RandCharFrom(string s)
    {
        int i = UnityEngine.Random.Range(0, s.Length);
        i = i < 0 ? 0 : i >= s.Length ? s.Length - 1 : i;
        return s[i];
    }

    internal string GenString()
    {
        string x = string.Empty;
        int syl = UnityEngine.Random.Range(4, 7);
        while (x.Length < syl)
        {
            x += RandCharFrom("bcdfghjklmnpqrstvwxyz");
            x += RandCharFrom("aeiou");
            if (UnityEngine.Random.Range(0, 10) > 7)
            {
                x += RandCharFrom("nsr");
            }
        }
      
        return x;
    }

    internal void DoCloudSave()
    {
        string word = GenString();

        SetStandBy("Saving string to cloud: " + word);
        PlayGamesPlatform p = (PlayGamesPlatform)Social.Active;
        p.UpdateState(0, System.Text.ASCIIEncoding.Default.GetBytes(word), this);
        EndStandBy();
        Status = "Saved string to cloud: " + word;
        mLastLocalSave = word;
        Logger.d("Saved string: " + word);
        ShowEffect(true);
    }

    private void DoCloudLoad()
    {
        mHadCloudConflict = false;
        SetStandBy("Loading from cloud...");
        ((PlayGamesPlatform)Social.Active).LoadState(0, this);
    }

    private void DoQuickGame(uint players)
    {
        uint opponents = players - 1;
        SetStandBy("Starting quick game " + players + " players...");
        PlayGamesPlatform.Instance.RealTime.CreateQuickGame(opponents, opponents, 0, this);
    }

    private void DoCreateGame()
    {
        SetStandBy("Creating game...");
        PlayGamesPlatform.Instance.RealTime.CreateWithInvitationScreen(1, 3, 0, this);
    }

    private void DoAcceptFromInbox()
    {
        SetStandBy("Showing inbox...");
        PlayGamesPlatform.Instance.RealTime.AcceptFromInbox(this);
    }

    private void DoAcceptIncoming()
    {
        if (mLastInvitationId == null)
        {
            Status = "No incoming invitation!";
            return;
        }
      
        SetStandBy("Accepting invitation...");
        PlayGamesPlatform.Instance.RealTime.AcceptInvitation(mLastInvitationId, this);
    }

    private void DoDeclineIncoming()
    {
        if (mLastInvitationId == null)
        {
            Status = "No incoming invitation!";
            return;
        }
      
        PlayGamesPlatform.Instance.RealTime.DeclineInvitation(mLastInvitationId);
        Status = "Declined incoming invitation.";
    }

    private void DoBroadcastMessage()
    {
        string word = GenString();

        bool reliable = UnityEngine.Random.Range(0, 2) == 0;

        PlayGamesPlatform.Instance.RealTime.SendMessageToAll(reliable, System.Text.ASCIIEncoding.Default.GetBytes(word));
        Status = "Sent message: " + word;
    }

    private void DoSendMessage()
    {
        string word = GenString();
        var connected = PlayGamesPlatform.Instance.RealTime.GetConnectedParticipants();
        var self = PlayGamesPlatform.Instance.RealTime.GetSelf();

        var nonSelf = connected.Where(p => !p.Equals(self)).ToList();

        bool reliable = UnityEngine.Random.Range(0, 2) == 0;
        var recipient = nonSelf[UnityEngine.Random.Range(0, nonSelf.Count)];

        PlayGamesPlatform.Instance.RealTime.SendMessage(
            reliable,
            recipient.ParticipantId, 
            System.Text.ASCIIEncoding.Default.GetBytes(word));
        Status = string.Format("Sent message: {0}, reliable: {1}, recipient: {2}", word, reliable, recipient.ParticipantId);
    }

    private void DoLeaveRoom()
    {
        Status = "Requested to leave room.";
        PlayGamesPlatform.Instance.RealTime.LeaveRoom();
    }

    private void DoListParticipants()
    {
        List<Participant> participants = PlayGamesPlatform.Instance.RealTime.GetConnectedParticipants();
        if (participants == null)
        {
            Status = "Participants: (null list)";
            return;
        }
      
        Status = string.Format("{0} participants.", participants.Count);
        Participant self = PlayGamesPlatform.Instance.RealTime.GetSelf();
        foreach (Participant p in participants)
        {
            if (self.ParticipantId.Equals(p.ParticipantId))
            {
                Status += "*";
            }
          
            Status += p.DisplayName + "(" + p.ParticipantId + ") ";
            Debug.Log(">>> participant: " + p.ToString());
        }
    }

    private void OnInvitationReceived(Invitation invitation, bool fromNotification)
    {
        string inviterName = invitation.Inviter != null ? invitation.Inviter.DisplayName : "(null)";
        Status = "!!! Got invitation " + (fromNotification ? " (from notification):" : ":") +
        " from " + inviterName + ", id " + invitation.InvitationId;
        mLastInvitationId = invitation.InvitationId;
    }

    private void OnMatchFromNotification(TurnBasedMatch match, bool fromNotification)
    {
        if (fromNotification)
        {
            mUi = Ui.TbmpMatch;
            mMatch = match;
            Status = "Got match from notification! " + match;
        }
        else
        {
            Status = "Got match a update not from notification.";
        }
    }

    private void DoTbmpQuickGame()
    {
        SetStandBy("Creating TBMP quick match...");
        PlayGamesPlatform.Instance.TurnBased.CreateQuickMatch(
            1,
            1,
            0,
            (bool success, TurnBasedMatch match) =>
            {
                ShowEffect(success);
                EndStandBy();
                mMatch = match;
                Status = success ? "Match created" : "Match creation failed";
                if (success)
                {
                    mUi = Ui.TbmpMatch;
                }
            });
    }

    private void DoTbmpCreateGame()
    {
        SetStandBy("Creating TBMP match...");
        PlayGamesPlatform.Instance.TurnBased.CreateWithInvitationScreen(
            1,
            7,
            0,
            (bool success, TurnBasedMatch match) =>
            {
                ShowEffect(success);
                EndStandBy();
                mMatch = match;
                Status = success ? "Match created" : "Match creation failed";
                if (success)
                {
                    mUi = Ui.TbmpMatch;
                }
            });
    }

    private void DoTbmpAcceptFromInbox()
    {
        SetStandBy("Accepting TBMP from inbox...");
        PlayGamesPlatform.Instance.TurnBased.AcceptFromInbox((bool success, TurnBasedMatch match) =>
            {
                ShowEffect(success);
                EndStandBy();
                mMatch = match;
                Status = success ? "Successfully accepted from inbox!" : "Failed to accept from inbox";
                if (success)
                {
                    mUi = Ui.TbmpMatch;
                }
            });
    }

    private void DoTbmpAcceptIncoming()
    {
        if (mLastInvitationId == null)
        {
            Status = "No incoming invitation received from listener.";
            return;
        }
      
        SetStandBy("Accepting TBMP invitation...");
        PlayGamesPlatform.Instance.TurnBased.AcceptInvitation(
            mLastInvitationId,
            (bool success, TurnBasedMatch match) =>
            {
                ShowEffect(success);
                EndStandBy();
                mMatch = match;
                Status = success ? "Successfully accepted invitation!" :
                "Failed to accept invitation";
                if (success)
                {
                    mUi = Ui.TbmpMatch;
                }
            });
    }

    private void DoTbmpDeclineIncoming()
    {
        if (mLastInvitationId == null)
        {
            Status = "No incoming invitation received from listener.";
            return;
        }
      
        PlayGamesPlatform.Instance.TurnBased.DeclineInvitation(mLastInvitationId);
        mLastInvitationId = null;
        Status = "Declined invitation.";
    }

    private string GetMatchSummary()
    {
        string summary = string.Empty;

        if (mMatch == null)
        {
            return "(null)";
        }

        string data = "(null)";
        if (mMatch.Data != null)
        {
            data = System.Text.ASCIIEncoding.Default.GetString(mMatch.Data);
        }

        summary = "Match: [" + data + "], S:" + mMatch.Status + ", T:" + mMatch.TurnStatus + "\n";
        summary += "With: ";
        foreach (Participant p in mMatch.Participants)
        {
            summary += " " + p.DisplayName;
        }
      
        summary += " and " + mMatch.AvailableAutomatchSlots + " pending automatch";
        return summary;
    }

    private void DoTbmpShowMatchData()
    {
        if (mMatch == null)
        {
            Status = "No match is active!";
            return;
        }

        Status = mMatch.ToString();
    }

    // figure out who is next to play
    private string GetNextToPlay(TurnBasedMatch match)
    {
        if (mMatch.AvailableAutomatchSlots > 0)
        {
            // next to play is an automatch player
            return null;
        }

        // WARNING: The following code for determining "who is next" MUST NOT BE USED
        // in a production game. It is here for debug purposes only. This code will
        // not take into account the order in which the first round (while there were
        // automatch slots open) was played, and will always produce round-robin next
        // participants based on the participant ID, which might make the second (and
        // subsequent) rounds have a different play order than the first round, which is,
        // for most games, a very bad experience.
        //
        // In your production game, consider storing the play order in the match data
        // to help determine who plays next.

        // what is my index in the list of participants?
        int index = -1;
        List<Participant> participants = mMatch.Participants;

        for (int i = 0; i < participants.Count; i++)
        {
            Participant p = participants[i];
            if (p.ParticipantId.Equals(mMatch.SelfParticipantId))
            {
                index = i;
                break;
            }
        }

        GooglePlayGames.OurUtils.Logger.d("My index = " + index);

        // who is the next participant in the Joined state?
        for (int j = 1; j <= participants.Count; j++)
        {
            Participant p = participants[(index + j) % participants.Count];
            if (p.Status == Participant.ParticipantStatus.Joined ||
                    p.Status == Participant.ParticipantStatus.Invited ||
                    p.Status == Participant.ParticipantStatus.NotInvitedYet)
            {
                GooglePlayGames.OurUtils.Logger.d("Using index = " + ((index + j) % participants.Count));
                return p.ParticipantId;
            }
        }

        Debug.LogError("*** ERROR: Failed to get next participant to play. No one available.");
        return null;
    }

    private void DoTbmpTakeTurn()
    {
        if (mMatch == null)
        {
            Status = "No match is active.";
            return;
        }
      
        if (mMatch.TurnStatus != TurnBasedMatch.MatchTurnStatus.MyTurn)
        {
            Status = "Not my turn.";
            return;
        }

        SetStandBy("Taking turn...");
        PlayGamesPlatform.Instance.TurnBased.TakeTurn(
            mMatch,
            System.Text.ASCIIEncoding.Default.GetBytes(GenString()),
            GetNextToPlay(mMatch),
            (bool success) =>
            {
                EndStandBy();
                ShowEffect(success);
                Status = success ? "Successfully took turn." : "Failed to take turn.";
                if (success)
                {
                    mMatch = null;
                    mUi = Ui.Tbmp;
                }
            });
    }

    private void DoTbmpFinish()
    {
        if (mMatch == null)
        {
            Status = "No match is active.";
            return;
        }
      
        if (mMatch.TurnStatus != TurnBasedMatch.MatchTurnStatus.MyTurn)
        {
            Status = "Not my turn.";
            return;
        }

        // I win; every one else loses
        MatchOutcome outcome = new MatchOutcome();
        foreach (Participant p in mMatch.Participants)
        {
            if (p.ParticipantId.Equals(mMatch.SelfParticipantId))
            {
                outcome.SetParticipantResult(p.ParticipantId, MatchOutcome.ParticipantResult.Win, 1);
            }
            else
            {
                outcome.SetParticipantResult(p.ParticipantId, MatchOutcome.ParticipantResult.Loss, 2);
            }
        }

        SetStandBy("Finishing match...");
        PlayGamesPlatform.Instance.TurnBased.Finish(
            mMatch,
            System.Text.ASCIIEncoding.Default.GetBytes("the end!"),
            outcome,
            (bool success) =>
            {
                EndStandBy();
                ShowEffect(success);
                Status = success ? "Successfully finished match." : "Failed to finish match.";
                if (success)
                {
                    mMatch = null;
                    mUi = Ui.Tbmp;
                }
            });
    }

    private void DoTbmpAckFinish()
    {
        if (mMatch == null)
        {
            Status = "No match is active.";
            return;
        }
      
        if (mMatch.Status != TurnBasedMatch.MatchStatus.Complete)
        {
            Status = "Match is not complete";
            return;
        }

        SetStandBy("Ack'ing finished match");
        PlayGamesPlatform.Instance.TurnBased.AcknowledgeFinished(
            mMatch,
            (bool success) =>
            {
                EndStandBy();
                ShowEffect(success);
                Status = success ? "Successfully ack'ed finish." : "Failed to ack finish.";
                if (success)
                {
                    mMatch = null;
                    mUi = Ui.Tbmp;
                }
            });
    }

    private void DoTbmpLeave()
    {
        if (mMatch == null)
        {
            Status = "No match is active.";
            return;
        }
      
        if (mMatch.TurnStatus == TurnBasedMatch.MatchTurnStatus.MyTurn)
        {
            Status = "It's my turn; use 'Leave During Turn'.";
            return;
        }

        SetStandBy("Leaving match...");
        PlayGamesPlatform.Instance.TurnBased.Leave(
            mMatch,
            (bool success) =>
            {
                EndStandBy();
                ShowEffect(success);
                Status = success ? "Successfully left match." : "Failed to leave match.";
                if (success)
                {
                    mMatch = null;
                    mUi = Ui.Tbmp;
                }
            });
    }

    private void DoTbmpLeaveDuringTurn()
    {
        if (mMatch == null)
        {
            Status = "No match is active.";
            return;
        }
      
        if (mMatch.TurnStatus != TurnBasedMatch.MatchTurnStatus.MyTurn)
        {
            Status = "It's not my turn.";
            return;
        }

        SetStandBy("Leaving match during turn...");
        PlayGamesPlatform.Instance.TurnBased.LeaveDuringTurn(
            mMatch,
            GetNextToPlay(mMatch),
            (bool success) =>
            {
                EndStandBy();
                ShowEffect(success);
                Status = success ? "Successfully left match during turn." :
                "Failed to leave match during turn.";
                if (success)
                {
                    mMatch = null;
                    mUi = Ui.Tbmp;
                }
            });
    }

    private void DoTbmpCancel()
    {
        if (mMatch == null)
        {
            Status = "No match is active.";
            return;
        }
      
        if (mMatch.Status != TurnBasedMatch.MatchStatus.Active)
        {
            Status = "Match is not active.";
            return;
        }

        SetStandBy("Cancelling match...");
        PlayGamesPlatform.Instance.TurnBased.Cancel(
            mMatch,
            (bool success) =>
            {
                EndStandBy();
                ShowEffect(success);
                Status = success ? "Successfully cancelled match." : "Failed to cancel match.";
                if (success)
                {
                    mMatch = null;
                    mUi = Ui.Tbmp;
                }
            });
    }

    private void DoTbmpRematch()
    {
        if (mMatch == null)
        {
            Status = "No match is active.";
            return;
        }
      
        if (!mMatch.CanRematch)
        {
            Status = "Match can't be rematched.";
            return;
        }

        SetStandBy("Rematching match...");
        PlayGamesPlatform.Instance.TurnBased.Rematch(
            mMatch,
            (bool success, TurnBasedMatch match) =>
            {
                EndStandBy();
                ShowEffect(success);
                mMatch = match;
                Status = success ? "Successfully rematched." : "Failed to rematch.";
                if (success)
                {
                    // if we succeed, it will be our turn, so go to the appropriate UI
                    mUi = Ui.TbmpMatch;
                }
            });
    }
}
