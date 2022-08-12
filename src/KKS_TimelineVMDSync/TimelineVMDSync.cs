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
using Unity.Console;
using HarmonyLib;
using Microsoft.Scripting.Hosting;

[BepInProcess("CharaStudio")]
[BepInPlugin(GUID, PluginName, Version)]
[BepInDependency("com.joan6694.illusionplugins.timeline")]
[BepInDependency(KKSVMDPlugin.GUID, BepInDependency.DependencyFlags.SoftDependency)]
public class TimelineVMDSync : BaseUnityPlugin
{
    public const string PluginName = "Timeline VMD Sync";
    public const string GUID = "org.njaecha.plugins.timelinevmdsync";
    public const string Version = "3.0.2";

    internal static new ManualLogSource Logger;

    private Rect windowRect = new Rect(500, 20, 240, 430);
    private GameObject container;
    private VMDAnimationMgr VMDPlayAnimMgr;
    private bool realTimeSync;
    private bool uiActive = true;
    private bool stupidFix = false;
    private ConfigEntry<bool> hotkeyActive;
    private bool uiHide = false;
    private bool showHotkeyUI = false;
    private bool syncVMDPlay = false;
    private bool syncMMDD = false;
    private float MMDDPlaybackSpeed = -1f;
    private float MMDDPlaybackLength = -1f;
    private bool VNGEfound = false;

    private ScriptEngine VNGEMainEninge;
    private ScriptScope VNGEScope;

    private CompiledCode gotoFrameCodeCompiled = null;
    private CompiledCode startCodeCompiled = null;
    private CompiledCode stopCodeCompiled = null;

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
    private ConfigEntry<bool> use30InsteadOfplayFPS;

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
        use30InsteadOfplayFPS = Config.Bind("MMDD", "Use 30 instead of playFPS", false, "Enable this to always use the default value of 30 when calculating the time for MMDD. You might want to use this if the sync button does not sync up properly");

    }
    void Start()
    {
        // grab the VMDPlay instance
        container = GameObject.Find("KKS_VMDPlayPlugin");
        if (container != null)
            VMDPlayAnimMgr = container.GetComponentInChildren<VMDAnimationMgr>();

        try
        {
            // grab the Scriping Engine for VNGE and create a scope for it
            VNGEMainEninge = Traverse.Create(typeof(Program)).Property("MainEngine").GetValue() as ScriptEngine;
            VNGEScope = VNGEMainEninge.CreateScope();
            VNGEfound = true;
        }
        catch
        {
            VNGEfound = false;
        }

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

        // for whatever reason I have to display the UI once BEFORE the user can open MMDD or else the checkbox character (☑️/☐) on the buttons is not displayed properly (wtf?)
        if (!stupidFix)
        {
            uiActive = false;
            stupidFix = true;
        }
    }

    private void WindowFunction(int windowID)
    {
        windowRect.height = 205;

        if (VMDPlayAnimMgr == null) GUI.enabled = false;
        if (GUI.Button(new Rect(10, 20, 105, 25), syncVMDPlay ? "☑️ VMDPlay" : "☐ VMDPlay"))
        {
            syncVMDPlay = !syncVMDPlay;
        }
        GUI.enabled = true;
        if (!VNGEfound) GUI.enabled = false;
        if (GUI.Button(new Rect(125, 20, 105, 25), syncMMDD ? "☑️ MMDD" : "☐ MMDD"))
        {
            MMDDPlaybackLength = retrieveMMDDPlaybackLength();
            // do not activate the MMDD mode if MMDD was not started (when retrieve method failed)
            if (MMDDPlaybackLength != -1f)
            {
                syncMMDD = !syncMMDD;
            }
        }
        GUI.enabled = true;
        if (GUI.Button(new Rect(10, 50, 40, 30), "<"))
            SyncPrevFrame();
        if (GUI.Button(new Rect(190, 50, 40, 30), ">"))
            SyncNextFrame();
        if (realTimeSync)
            GUI.enabled = false;
        if (GUI.Button(new Rect(55, 50, 130, 30), Timeline.Timeline.isPlaying ? "Pause" : "Play"))
            SyncPlayPause();
        if (GUI.Button(new Rect(125, 85, 105, 25), "Sync"))
            SyncPlayers();
        GUI.enabled = true;
        if (GUI.Button(new Rect(10, 85, 105, 25), "Stop"))
            SyncStop();
        if (!syncMMDD && !syncVMDPlay) GUI.enabled = false;
        if (GUI.Button(new Rect(10, 115, 220, 25), realTimeSync ? "☑️ Auto Sync" : "☐ Auto Sync"))
        {
            realTimeSync = !realTimeSync;
        }
        GUI.enabled = true;
        showHotkeyUI = GUI.Toggle(new Rect(10, 145, 220, 25), showHotkeyUI, showHotkeyUI ? "Close Hotkeylist" : "Show Hotkeylist", GUI.skin.button);
        if (showHotkeyUI)
        {
            float _base = windowRect.height - 40; // start of the expandable UI section
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

        // display if either Timeline and VMD don't match or Timeline and MMDD don't match and the respecitve modes are active. 
        if ((VMDPlayAnimMgr != null
            && ((Timeline.Timeline.duration - VMDPlayAnimMgr.AnimationLength > 1)
            || (VMDPlayAnimMgr.AnimationLength - Timeline.Timeline.duration > 1))
            && syncVMDPlay)
            || (((Timeline.Timeline.duration - MMDDPlaybackLength > 1)
            || (MMDDPlaybackLength - Timeline.Timeline.duration > 1))
            && syncMMDD))
        {
            float _base = windowRect.height - 35;
            windowRect.height = windowRect.height + 75;
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.normal.textColor = Color.yellow;
            GUI.Label(new Rect(10, _base, 220, 40), "WARNING: duration/length not matching:", style);
            GUI.Label(new Rect(10, _base + 35, 110, 20), "Timeline:");
            GUI.Label(new Rect(75, _base + 35, 110, 20), $"{TimeSpan.FromSeconds(Timeline.Timeline.duration).Minutes}:{TimeSpan.FromSeconds(Timeline.Timeline.duration).Seconds}");

            int _offset = 0;
            if (syncVMDPlay)
            {
                GUI.Label(new Rect(10, _base + 55, 110, 20), "VMDPlay:");
                GUI.Label(new Rect(75, _base + 55, 110, 20), $"{TimeSpan.FromSeconds(VMDPlayAnimMgr.AnimationLength).Minutes}:{TimeSpan.FromSeconds(VMDPlayAnimMgr.AnimationLength).Seconds}");
                _offset = 20;
            }
            if (syncMMDD)
            {
                windowRect.height = windowRect.height + _offset;
                GUI.Label(new Rect(10, _base + _offset + 55, 110, 20), "MMDD:");
                GUI.Label(new Rect(75, _base + _offset + 55, 110, 20), $"{TimeSpan.FromSeconds(MMDDPlaybackLength).Minutes}:{TimeSpan.FromSeconds(MMDDPlaybackLength).Seconds}");
                if (GUI.Button(new Rect(150, _base + 35, 80, 40 + _offset), "Update\nMMDD"))
                {
                    MMDDPlaybackLength = retrieveMMDDPlaybackLength();
                }
            }
        }
        if (GUI.Button(new Rect(10, windowRect.height - 30, 220, 25), $"HIDE [{uiHideSC.Value.ToString()}]"))
            uiHide = true;
        GUI.DragWindow();
    }

    public float retrieveMMDDPlaybackLength()
    {
        float length = -1f;

        // try block in case MMDD was not yet started and it would throw and error.
        try
        {
            // code to retrive playFPS and totalFrame
            string code1 = "from vngameengine import vnge_game\nplayback = vnge_game.gdata.mmdd.playFPS\nframes = vnge_game.gdata.mmdd.totalFrame";
            // create script, compile and execute it with the scope
            VNGEMainEninge.CreateScriptSourceFromString(code1).Compile().Execute(VNGEScope);
            // use scope to retrieve the values of playFPS and totalFrame
            MMDDPlaybackSpeed = Convert.ToSingle(VNGEScope.GetVariable("playback"));
            length = Convert.ToSingle(VNGEScope.GetVariable("frames")) / MMDDPlaybackSpeed;
        }
        catch
        {
            // if the method failed then MMDD was most likely not running yet
            Logger.LogWarning("Error while retrieving MMDD information");
            Logger.LogMessage("Please make sure MMDD is started in your scene!");
            syncMMDD = false;
            length = -1f;
        }

        return length;
    }

    public void SyncPlayPause()
    {
        if (!Timeline.Timeline.isPlaying)
        {
            Timeline.Timeline.Play();

            if (syncVMDPlay && !realTimeSync)
                VMDPlayAnimMgr.PlayAll();
            if (syncMMDD && !realTimeSync)
            {

                if (startCodeCompiled == null)
                {
                    // python code to call the start function
                    string code = "from vngameengine import vnge_game\nvnge_game.gdata.mmdd.start()";
                    // cache compiled script
                    startCodeCompiled = VNGEMainEninge.CreateScriptSourceFromString(code).Compile();
                }
                startCodeCompiled.Execute();
            }
        }
        else
        {
            Timeline.Timeline.Pause();
            if (syncVMDPlay && !realTimeSync)
                VMDPlayAnimMgr.PauseAll();
            if (syncMMDD && !realTimeSync)
            {
                if (stopCodeCompiled == null)
                {
                    // python code to call the stop function
                    string code = "from vngameengine import vnge_game\nvnge_game.gdata.mmdd.stop()";
                    // cache compiled script
                    stopCodeCompiled = VNGEMainEninge.CreateScriptSourceFromString(code).Compile();
                }
                stopCodeCompiled.Execute();
            }

            SyncPlayers();
        }
    }
    public void SyncNextFrame()
    {
        Timeline.Timeline.NextFrame();
        SyncPlayers();
    }
    public void SyncPrevFrame()
    {
        Timeline.Timeline.PreviousFrame();
        SyncPlayers();
    }
    public void SyncStop()
    {
        Timeline.Timeline.Stop();
        if (syncVMDPlay)
        {
            VMDPlayAnimMgr.StopAll();
            VMDPlayAnimMgr.PlayAll();
            VMDPlayAnimMgr.PauseAll();
            VMDPlayAnimMgr.StopAll();
        }
        if (syncMMDD)
        {
            // python code to call the stop function
            string code = "from vngameengine import vnge_game\nvnge_game.gdata.mmdd.gotoStartFrame()\nvnge_game.gdata.mmdd.stop()";
            // create script, compile and execute
            VNGEMainEninge.CreateScriptSourceFromString(code).Compile().Execute();
        }
        
    }
    public void SyncPlayers()
    {
        if (syncVMDPlay)
        {
            if (VMDPlayAnimMgr.AnimationPosition == 0)
            {
                VMDPlayAnimMgr.PlayAll();                 // play and instantly pause the vmd timeline to "unfreeze" if in "stop" status 
                VMDPlayAnimMgr.PauseAll();                // otherwise the AnimationPosistion can not be set
            }
            VMDPlayAnimMgr.AnimationPosition = Timeline.Timeline.playbackTime;
        }
        if (syncMMDD)
        {
            // calculate frame which represents the current time
            int frame;
            if (use30InsteadOfplayFPS.Value)
                frame = (int)(Timeline.Timeline.playbackTime * 30);
            else
                frame = (int)(Timeline.Timeline.playbackTime * MMDDPlaybackSpeed);

            if(gotoFrameCodeCompiled == null)
            {
                // python code to goto the specified frame and retrieve playFPS (in case it has changed)
                string code = $"from vngameengine import vnge_game\nvnge_game.gdata.mmdd.gotoFrame(frame)\ncurrentPlayback = vnge_game.gdata.mmdd.playFPS";
                // cache compiled code
                gotoFrameCodeCompiled = VNGEMainEninge.CreateScriptSourceFromString(code).Compile();
            }
            // inject frame variable with the calculated frame value
            VNGEScope.SetVariable("frame", frame);
            // execute gotoFrame script with the scope
            gotoFrameCodeCompiled.Execute(VNGEScope);
            // retrieve playFPS value and change incase it has change
            float newPlayback = Convert.ToSingle(VNGEScope.GetVariable("currentPlayback"));
            if (MMDDPlaybackSpeed != newPlayback)
            {
                MMDDPlaybackSpeed = newPlayback;
                SyncPlayers();
            }
        }
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

        if (realTimeSync && Timeline.Timeline.playbackTime != VMDPlayAnimMgr.AnimationPosition)
            SyncPlayers();

        if (uiHideSC.Value.IsDown())
            uiHide = !uiHide;

        if (hotkeyActive.Value)
        {
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
            if (stopSC.Value.IsDown())
                SyncStop();
            if (realTimeSync)
                return; 
            if (playPauseSC.Value.IsDown())
                SyncPlayPause();
            if (syncSC.Value.IsDown() && VMDPlayAnimMgr.AnimationPosition != Timeline.Timeline.playbackTime)
                SyncPlayers();
        }
    }
}
