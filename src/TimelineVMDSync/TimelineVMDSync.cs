using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using Timeline;
using KKAPI;
using KKVMDPlayPlugin;

[BepInProcess("CharaStudio")]
[BepInPlugin(GUID, PluginName, Version)]
[BepInDependency("com.joan6694.illusionplugins.timeline")]
[BepInDependency(KKVMDPlugin.GUID)]
public class TimelineVMDSync : BaseUnityPlugin
{
    public const string PluginName = "Timeline VMD Sync";
    public const string GUID = "org.njaecha.plugins.timelinevmdsync";
    public const string Version = "2.0.0";

    internal static new ManualLogSource Logger;

    private Rect windowRect = new Rect(500, 20, 240, 430);
    private GameObject container;
    private VMDAnimationMgr animationMgr;
    private bool realTimeSync;
    private bool uiActive = false;
    private ConfigEntry<bool> hotkeyActive;
    private bool uiHide = false;
    private bool showHotkeyUI = false;

    private ConfigEntry<KeyboardShortcut> playPauseSC;
    private ConfigEntry<KeyboardShortcut> stopSC;
    private ConfigEntry<KeyboardShortcut> syncSC;
    private ConfigEntry<KeyboardShortcut> autoSyncSC;
    private ConfigEntry<KeyboardShortcut> uiOpenSC;
    private ConfigEntry<KeyboardShortcut> uiHideSC;
    private ConfigEntry<KeyboardShortcut> nextFrameSC;
    private ConfigEntry<KeyboardShortcut> prevFrameSC;
    private ConfigEntry<KeyboardShortcut> nextFrameHoldSC;
    private ConfigEntry<KeyboardShortcut> prevFrameHoldSC;

    void Awake()
    {
        Logger = base.Logger;

        hotkeyActive = Config.Bind("General", "Hotkey Active", true, "Toggle if hotkeys are active");

        playPauseSC = Config.Bind("Keyboard shortcuts", "Play/Pause", new KeyboardShortcut(KeyCode.K), "Hotkey that controlls play/pause");
        stopSC = Config.Bind("Keyboard shortcuts", "Stop", new KeyboardShortcut(KeyCode.M), "Hotkey that controlls stop");
        syncSC = Config.Bind("Keyboard shortcuts", "Sync", new KeyboardShortcut(KeyCode.Comma), "Hotkey that syncs VMDPlay to Timline");
        autoSyncSC = Config.Bind("Keyboard shortcuts", "AutoSync", new KeyboardShortcut(KeyCode.Comma, KeyCode.LeftShift), "Hotkey that toggles autosync");
        uiOpenSC = Config.Bind("Keyboard shortcuts", "Open UI", new KeyboardShortcut(KeyCode.N, KeyCode.LeftShift, KeyCode.LeftControl), "Open/Close the UI");
        uiHideSC = Config.Bind("Keyboard shortcuts", "Hide UI", new KeyboardShortcut(KeyCode.N, KeyCode.LeftAlt), "Hide/Unhide the UI");
        nextFrameSC = Config.Bind("Keyboard shortcuts", "Next Frame", new KeyboardShortcut(KeyCode.L), "Jump one frame forward");
        prevFrameSC = Config.Bind("Keyboard shortcuts", "Previous Frame", new KeyboardShortcut(KeyCode.J), "Jump one frame backwards");
        nextFrameHoldSC = Config.Bind("Keyboard shortcuts", "Next Frame (Hold Down)", new KeyboardShortcut(KeyCode.L, KeyCode.LeftAlt), "Jumps one Timeline-frame forwards per game-frame");
        prevFrameHoldSC = Config.Bind("Keyboard shortcuts", "Previous Frame (Hold Down)", new KeyboardShortcut(KeyCode.J, KeyCode.LeftAlt), "Jump one Timeline-frame backwards pre game-frame");

    }
    void Start()
    {
        container = GameObject.Find("KK_VMDPlayPlugin");
        animationMgr = container.GetComponentInChildren<VMDAnimationMgr>();
    }
    void OnGUI()
    {
        if (!uiActive)
            return;
        if (uiHide)
            return;
        else
        {
            windowRect = GUI.Window(77347, windowRect, WindowFunction, $"Timeline VMD Sync v{Version}");
            KKAPI.Utilities.IMGUIUtils.EatInputInRect(windowRect);
        }
    }

    private void WindowFunction(int windowID)
    {
        windowRect.height = 200;
        GUIStyle timeStyle = new GUIStyle(GUI.skin.label);
        timeStyle.alignment = TextAnchor.MiddleCenter;
        GUI.Label(new Rect(10, 20, 95, 20), Timeline.Timeline.playbackTime.ToString(), timeStyle);
        GUI.Label(new Rect(105, 20, 30, 20), "|", timeStyle);
        GUI.Label(new Rect(135, 20, 95, 20), animationMgr.AnimationPosition.ToString(), timeStyle);


        if (GUI.Button(new Rect(10, 50, 40, 20), "<"))
            SyncPrevFrame();
        if (GUI.Button(new Rect(55, 50, 130, 20), Timeline.Timeline.isPlaying ? "Pause" : "Play"))
            SyncPlayPause();
        if (GUI.Button(new Rect(190, 50, 40, 20), ">"))
            SyncNextFrame();
        if (GUI.Button(new Rect(10, 80, 105, 20), "Stop"))
            SyncStop();
        if (GUI.Button(new Rect(125, 80, 105, 20), "Sync"))
            SyncVMD();
        realTimeSync = GUI.Toggle(new Rect(10, 110, 220, 20), realTimeSync, " AutoSync");
        showHotkeyUI = GUI.Toggle(new Rect(10, 140, 220, 20), showHotkeyUI, showHotkeyUI ? "Close Hotkeylist" : "Show Hotkeylist", GUI.skin.button);
        if (showHotkeyUI)
        {
            float _base = windowRect.height - 40;
            windowRect.height = windowRect.height + 130;
            GUI.Label(new Rect(10, _base + 10, 100, 130),
                    $"Play/Pause:   \n" +
                    $"Stop:         \n" +
                    $"Sync:         \n" +
                    $"AutoSync:     \n" +
                    $"Next Frame:   \n" +
                    $"Prev. Frame:  \n" +
                    $"Next F. (Hold): \n" +
                    $"Prev F. (Hold):"
                );
            GUI.Label(new Rect(110, _base + 10, 120, 130),
                    $"[{playPauseSC.Value.ToString()}]\n" +
                    $"[{stopSC.Value.ToString()}]\n" +
                    $"[{syncSC.Value.ToString()}]\n" +
                    $"[{autoSyncSC.Value.ToString()}]\n" +
                    $"[{nextFrameSC.Value.ToString()}]\n" +
                    $"[{prevFrameSC.Value.ToString()}]\n" +
                    $"[{nextFrameHoldSC.Value.ToString()}]\n" +
                    $"[{prevFrameHoldSC.Value.ToString()}]"
                );
        }


        if ((Timeline.Timeline.duration - animationMgr.AnimationLength > 1) || (animationMgr.AnimationLength - Timeline.Timeline.duration > 1))
        {
            float _base = windowRect.height - 40;
            windowRect.height = windowRect.height + 90;
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.normal.textColor = Color.yellow;
            style.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(10, _base, 220, 40), "WARNING:\ndurations not matching:", style);
            GUI.Label(new Rect(10, _base + 40, 110, 20), "Timeline:");
            GUI.Label(new Rect(110, _base + 40, 110, 20), $"{TimeSpan.FromSeconds(Timeline.Timeline.duration).Minutes}:{TimeSpan.FromSeconds(Timeline.Timeline.duration).Seconds}");
                
            GUI.Label(new Rect(10, _base + 60, 110, 20), "VMDPlay:");
            GUI.Label(new Rect(110, _base + 60, 110, 20), $"{TimeSpan.FromSeconds(animationMgr.AnimationLength).Minutes}:{TimeSpan.FromSeconds(animationMgr.AnimationLength).Seconds}");
        }
        if (GUI.Button(new Rect(10, windowRect.height - 30, 220, 20), $"HIDE [{uiHideSC.Value.ToString()}]"))
            uiHide = true;
        GUI.DragWindow();
    }

    public void SyncPlayPause()
    {
        if (!Timeline.Timeline.isPlaying)
        {
            Timeline.Timeline.Play();
            if (!realTimeSync)
                animationMgr.PlayAll();
        }
        else
        {
            Timeline.Timeline.Pause();
            if (!realTimeSync)
                animationMgr.PauseAll();
            SyncVMD();
        }
    }
    public void SyncNextFrame()
    {
        Timeline.Timeline.NextFrame();
        SyncVMD();
    }public void SyncPrevFrame()
    {
        Timeline.Timeline.PreviousFrame();
        SyncVMD();
    }
    public void SyncStop()
    {
        Timeline.Timeline.Stop();
        animationMgr.StopAll();
        animationMgr.PlayAll();
        animationMgr.PauseAll();
        animationMgr.StopAll();
    }
    public void SyncVMD()
    {
        if (animationMgr.AnimationPosition == 0)
        {
            animationMgr.PlayAll();                 //play and instantly pause the vmd timeline to "unfreeze" if in "stop" status 
            animationMgr.PauseAll();                //otherwise the AnimationPosistion can not be set
        }
        animationMgr.AnimationPosition = Timeline.Timeline.playbackTime;
    }

    void Update()
    {
        if (uiOpenSC.Value.IsDown())
        {
            if (!uiHide)
                uiActive = !uiActive;
            else
                uiHide = false;
        }

        if (!uiActive)
            return;

        if (realTimeSync && Timeline.Timeline.playbackTime != animationMgr.AnimationPosition)
            SyncVMD();

        if (uiHideSC.Value.IsDown())
            uiHide = !uiHide;

        if (hotkeyActive.Value)
        {
            if (playPauseSC.Value.IsDown())
                SyncPlayPause();
            if (stopSC.Value.IsDown())
                SyncStop();
            if (syncSC.Value.IsDown() && animationMgr.AnimationPosition != Timeline.Timeline.playbackTime)
                SyncVMD();
            if (autoSyncSC.Value.IsDown())
                realTimeSync = !realTimeSync;
            if (nextFrameSC.Value.IsDown())
                SyncNextFrame();
            if (prevFrameSC.Value.IsDown())
                SyncPrevFrame();
            if (nextFrameHoldSC.Value.IsPressed())
                SyncNextFrame();
            if (prevFrameHoldSC.Value.IsPressed())
                SyncPrevFrame();
        }
    }
}
