using System.Collections.Generic;
using System.Globalization;
using System.Collections;
using System.Linq;
using UnityEngine;
using Oxide.Core;
using Network;

namespace Oxide.Plugins
{
    [Info("Voice Troll", "Bazz3l", "1.0.7")]
    [Description("Troll players making them speak with recorded audio clips.")]
    class VoiceTroll : RustPlugin
    {
        #region Fields
        private const string _permUse = "voicetroll.use";
        private AudioClip _currentClip;
        private Coroutine _coroutine;
        private bool _recording;
        private uint _netId;
        private StoredData _stored;
        private static VoiceTroll Instance;
        #endregion

        #region Stored
        class StoredData
        {
            public List<AudioClip> AudioClips = new List<AudioClip>();
        }

        class AudioClip
        {
            public string ClipName;
            public List<byte[]> Data = new List<byte[]>();
            public static AudioClip FindByName(string clipName) => Instance._stored.AudioClips.Find(x => x.ClipName == clipName);
        }

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, _stored);
        #endregion

        #region Oxide
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string> {
                { "InvalidSyntax", "Invalid syntax: /vc <play|select|create|remove> <name>\n/vc record, toggle recording.\n/vc target <name|id>, set plaback target." },
                { "NoPermission", "No permission." },
                { "ClipNotFound", "Clip not found." },
                { "ClipExists", "Clip already exists." },
                { "ClipCreated", "{0} was created." },
                { "ClipRemoved", "{0} was removed." },
                { "ClipPlaying", "{0} is now playing." },
                { "ClipSelected", "{0} is now selected." },
                { "ClipProcessing", "A clip is already playing, please wait..." },
                { "TargetFound", "{0} is now the current playback target." },
                { "TargetNotFound", "No target found." },
                { "RecordToggle", "Recording {0}." }
            }, this);
        }

        private void OnServerInitialized()
        {
            permission.RegisterPermission(_permUse, this);
        }

        private void Init()
        {
            Instance = this;

            _stored = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);
        }

        private void Unload()
        {
            if (_coroutine != null)
            {
                InvokeHandler.Instance.StopCoroutine(_coroutine);
            }

            _coroutine = null;

            Instance = null;
        }

        private void OnPlayerVoice(BasePlayer player, byte[] data)
        {
            if (!permission.UserHasPermission(player.UserIDString, _permUse))
            {
                return;
            }

            if (_currentClip == null || !_recording)
            {
                return;
            }

            _currentClip.Data.Add(data);

            SaveData();
        }
        #endregion

        #region Core
        private IEnumerator RunVoiceQueue(AudioClip sound)
        {
            foreach (byte[] data in sound.Data)
            {
                SendVoiceData(data);

                yield return new WaitForSeconds(0.02f);
            }

            yield return new WaitForSeconds(0.02f);

            _coroutine = null;
        }

        private void SendVoiceData(byte[] data)
        {
            if (!Net.sv.write.Start())
            {
                return;
            }

            SendInfo info = new SendInfo();
            info.connections = BasePlayer.activePlayerList.Select(x => x.Connection).ToList();
            info.priority = Priority.Immediate;

            Net.sv.write.PacketID(Message.Type.VoiceData);
            Net.sv.write.UInt32(_netId);
            Net.sv.write.BytesWithSize(data);
            Net.sv.write.Send(info);
        }

        private void QueueClip(AudioClip sound)
        {
            _coroutine = InvokeHandler.Instance.StartCoroutine(RunVoiceQueue(sound));
        }

        private BasePlayer FindPlayerTarget(string nameOrId)
        {
            foreach (BasePlayer player in BasePlayer.allPlayerList)
            {
                if (player.IsSleeping()) continue;

                if (player.displayName.Contains(nameOrId, CompareOptions.IgnoreCase))
                {
                    return player;
                }
                else if (nameOrId == player.UserIDString)
                {
                    return player;
                }
            }

            return null;
        }

        private void PlayClip(BasePlayer player, params object[] args)
        {
            if (args.Length < 1)
            {
                player.ChatMessage(Lang("InvalidSyntax", player.UserIDString));
                return;
            }

            AudioClip sound = AudioClip.FindByName(string.Join("", args));
            if (sound == null)
            {
                player.ChatMessage(Lang("ClipNotFound", player.UserIDString));
                return;
            }

            if (_coroutine != null)
            {
                player.ChatMessage(Lang("ClipProcessing", player.UserIDString));
                return;
            }

            QueueClip(sound);

            player.ChatMessage(Lang("ClipPlaying", player.UserIDString, sound.ClipName));
        }

        private void SelectClip(BasePlayer player, params object[] args)
        {
            if (args.Length < 1)
            {
                player.ChatMessage(Lang("InvalidSyntax", player.UserIDString));
                return;
            }

            AudioClip sound = AudioClip.FindByName(string.Join("", args));
            if (sound == null)
            {
                player.ChatMessage(Lang("ClipNotFound", player.UserIDString));
                return;
            }

            _currentClip = sound;

            player.ChatMessage(Lang("ClipSelected", player.UserIDString, sound.ClipName));
        }

        private void RemoveClip(BasePlayer player, params object[] args)
        {
            if (args.Length < 1)
            {
                player.ChatMessage(Lang("InvalidSyntax", player.UserIDString));
                return;
            }

            AudioClip sound = AudioClip.FindByName(string.Join("", args));
            if (sound == null)
            {
                player.ChatMessage(Lang("ClipNotFound", player.UserIDString));
                return;
            }

            _stored.AudioClips.Remove(sound);

            player.ChatMessage(Lang("ClipRemoved", player.UserIDString, sound.ClipName));
        }

        private void CreateClip(BasePlayer player, params object[] args)
        {
            if (args.Length < 1)
            {
                player.ChatMessage(Lang("InvalidSyntax", player.UserIDString));
                return;
            }

            string clipName =  string.Join("", args);

            AudioClip sound = AudioClip.FindByName(clipName);
            if (sound != null)
            {
                player.ChatMessage(Lang("ClipExists", player.UserIDString));
                return;
            }

            sound = new AudioClip { ClipName = clipName };

            _stored.AudioClips.Add(sound);

            SaveData();

            _currentClip = sound;

            player.ChatMessage(Lang("ClipCreated", player.UserIDString, sound.ClipName));
        }

        private void ToggleRecord(BasePlayer player, params object[] args)
        {
            _recording = !_recording;

            player.ChatMessage(Lang("RecordToggle", player.UserIDString, (_recording ? "Enabled" : "Disabled")));
        }

        private void FindTarget(BasePlayer player, params object[] args)
        {
            if (args.Length < 1)
            {
                player.ChatMessage(Lang("InvalidSyntax", player.UserIDString));
                return;
            }

            BasePlayer target = FindPlayerTarget(string.Join(" ", args));
            if (target == null)
            {
                player.ChatMessage(Lang("TargetNotFound", player.UserIDString));
                return;
            }

            _netId = target.net.ID;

            player.ChatMessage(Lang("TargetFound", player.UserIDString, target.displayName));
        }
        #endregion

        #region Commands
        [ChatCommand("vc")]
        private void CommandVoiceClip(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, _permUse))
            {
                player.ChatMessage(Lang("NoPermission", player.UserIDString));
                return;
            }

            if (args.Length < 1)
            {
                player.ChatMessage(Lang("InvalidSyntax", player.UserIDString));
                return;
            }

            switch (args[0].ToLower())
            {
                case "play":
                    PlayClip(player, args.Skip(1).ToArray());
                    break;
                case "create":
                    CreateClip(player, args.Skip(1).ToArray());
                    break;
                case "select":
                    SelectClip(player, args.Skip(1).ToArray());
                    break;
                case "remove":
                    RemoveClip(player, args.Skip(1).ToArray());
                    break;
                case "target":
                    FindTarget(player, args.Skip(1).ToArray());
                    break;
                case "record":
                    ToggleRecord(player, args);
                    break;
                default:
                    player.ChatMessage(Lang("InvalidSyntax", player.UserIDString));
                    break;
            }
        }
        #endregion

        #region Helpers
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        #endregion
    }
}