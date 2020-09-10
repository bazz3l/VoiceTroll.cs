using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Oxide.Core;
using Network;

namespace Oxide.Plugins
{
    [Info("Voice Troll", "Bazz3l", "1.0.1")]
    [Description("Troll players making them speak with recorded audio clips.")]
    class VoiceTroll : RustPlugin
    {
        #region Fields
        Coroutine coroutine;
        StoredData stored;
        AudioClip currentClip;
        uint netId;
        bool recording;
        static VoiceTroll Instance;
        #endregion

        #region Stored
        public class StoredData
        {
            public List<AudioClip> AudioClips = new List<AudioClip>();
        }

        public class AudioClip
        {
            public string ClipName;
            public List<byte[]> Data = new List<byte[]>();
            public static AudioClip FindByName(string clipName) => Instance.stored.AudioClips.Find(x => x.ClipName == clipName);
        }

        public void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, stored);
        #endregion

        #region Oxide
        void Init()
        {
            Instance = this;

            stored = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);
        }

        void Unload()
        {
            if (coroutine != null)
            {
                InvokeHandler.Instance.StopCoroutine(coroutine);
            }

            coroutine = null;

            Instance = null;
        }

        void OnPlayerVoice(BasePlayer player, byte[] data)
        {
            if (currentClip == null || !recording || !player.IsAdmin)
            {
                return;
            }

            currentClip.Data.Add(data);

            SaveData();
        }
        #endregion

        #region Core
        IEnumerator<object> RunVoiceQueue(AudioClip sound)
        {
            foreach (byte[] data in sound.Data)
            {
                SendVoiceData(data);

                yield return new WaitForSeconds(0.02f);
            }

            yield return new WaitForSeconds(0.02f);

            coroutine = null;
        }

        void SendVoiceData(byte[] data)
        {
            if (!Net.sv.write.Start())
            {
                return;
            }

            SendInfo info = new SendInfo();
            info.connections = BasePlayer.activePlayerList.Select(x => x.Connection).ToList();
            info.priority = Priority.Immediate;

            Net.sv.write.PacketID(Message.Type.VoiceData);
            Net.sv.write.UInt32(netId);
            Net.sv.write.BytesWithSize(data);
            Net.sv.write.Send(info);
        }

        public void QueueClip(AudioClip sound)
        {
            coroutine = InvokeHandler.Instance.StartCoroutine(RunVoiceQueue(sound));
        }

        BasePlayer FindPlayerTarget(string nameOrId)
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

        void PlaySound(BasePlayer player, params object[] args)
        {
            if (args.Length < 1)
            {
                player.ChatMessage("Invalid syntax: /vc play <name>.");
                return;
            }

            AudioClip sound = AudioClip.FindByName(string.Join("", args));
            if (sound == null)
            {
                player.ChatMessage("No audio clip found by that name.");
                return;
            }

            if (coroutine != null)
            {
                player.ChatMessage("Already playing please wait...");
                return;
            }

            QueueClip(sound);

            player.ChatMessage($"Playing audio clip {sound.ClipName}.");
        }

        void SelectSound(BasePlayer player, params object[] args)
        {
            if (args.Length < 1)
            {
                player.ChatMessage("Invalid syntax: /vp select <name>.");
                return;
            }

            AudioClip sound = AudioClip.FindByName(string.Join("", args));
            if (sound == null)
            {
                player.ChatMessage("No audio clip found by that name.");
                return;
            }

            currentClip = sound;

            player.ChatMessage($"Playing audio clip {sound.ClipName}.");
        }

        void RemoveSound(BasePlayer player, params object[] args)
        {
            if (args.Length < 1)
            {
                player.ChatMessage("Invalid syntax: /vc remove <name>.");
                return;
            }

            AudioClip sound = AudioClip.FindByName(string.Join("", args));
            if (sound == null)
            {
                player.ChatMessage("No audio clip found by that name.");
                return;
            }

            stored.AudioClips.Remove(sound);

            player.ChatMessage("Audio clip was removed.");
        }

        void CreateSound(BasePlayer player, params object[] args)
        {
            if (args.Length < 1)
            {
                player.ChatMessage("Invalid syntax: /vc create <name>.");
                return;
            }

            string clipName =  string.Join("", args);

            AudioClip sound = AudioClip.FindByName(clipName);
            if (sound != null)
            {
                player.ChatMessage("Audio clip already exists.");
                return;
            }

            stored.AudioClips.Add(new AudioClip { ClipName = clipName });

            currentClip = sound;

            SaveData();

            player.ChatMessage($"Audio clip {clipName} was created.");
        }

        void RecordSound(BasePlayer player, params object[] args)
        {
            recording = !recording;

            player.ChatMessage($"Recording {(recording ? "Enabled" : "Disabled")}.");
        }

        void FindTarget(BasePlayer player, params object[] args)
        {
            if (args.Length < 1)
            {
                player.ChatMessage("Invalid syntax: /vc target <name|id>.");
                return;
            }

            BasePlayer target = FindPlayerTarget(string.Join(" ", args));
            if (target == null)
            {
                player.ChatMessage("No target found.");
                return;
            }

            netId = target.net.ID;

            player.ChatMessage("Audio playback target set.");
        }
        #endregion

        #region Commands
        [ChatCommand("vc")]
        void CommandVoiceClip(BasePlayer player, string command, string[] args)
        {
            if (args.Length < 1)
            {
                player.ChatMessage("Invalid syntax: /vc <play|select|create|remove> <name>, /vc record, /vc target <name|id>.");
                return;
            }

            switch (args[0].ToLower())
            {
                case "play":
                    PlaySound(player, args.Skip(1).ToArray());
                    break;
                case "create":
                    CreateSound(player, args.Skip(1).ToArray());
                    break;
                case "select":
                    SelectSound(player, args.Skip(1).ToArray());
                    break;
                case "remove":
                    RemoveSound(player, args.Skip(1).ToArray());
                    break;
                case "target":
                    FindTarget(player, args.Skip(1).ToArray());
                    break;
                case "record":
                    RecordSound(player, args);
                    break;
                default:
                    player.ChatMessage("Invalid syntax: /vc <play|select|create|remove> <name>, /vc record, /vc target <name|id>.");
                    break;
            }
        }
        #endregion
    }
}