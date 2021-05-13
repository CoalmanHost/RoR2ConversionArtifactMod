using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BepInEx;
using RoR2;
using RoR2.UI;
using RoR2.Achievements;
using R2API;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.Networking.NetworkSystem;
using UnityEngine.Networking.PlayerConnection;
using UnityEngine.UI;
using R2API.Utils;
using System.Reflection;
using System.IO;

namespace RoR2ConversionArtifactMod
{
    [R2APISubmoduleDependency("LoadoutAPI")]
    [BepInPlugin(modGuid, modName, modVersion)]
    public class ConversionArtifactMod : BaseUnityPlugin
    {
        public const string modGuid = "ConversionArtifactMod";
        public const string modName = "Artifact of Conversion mod";
        public const string modVersion = "1.0.0";
        
        ArtifactDef Conversion;

        static Dictionary<NetworkConnection, CharacterMaster> players;
        public static readonly short msgItemDropType = (short)(MsgType.Highest + modGuid.GetHashCode() + 3);
        public static readonly short msgEquipmentDropType = (short)(MsgType.Highest + modGuid.GetHashCode() + 2);
        public static readonly short msgDropCallbackType = (short)(MsgType.Highest + modGuid.GetHashCode() +1);

        static readonly NetworkMessageDelegate msgItemDropHandler = (NetworkMessage msg) =>
        {
            var itemIndex = msg.reader.ReadItemIndex();
            ServerStuffDropper.DropItem(players[msg.conn], itemIndex);
            var limits = players[msg.conn].GetComponent<InventoryLimits>();
            limits.Count(players[msg.conn].inventory);
            //msg.conn.Send(msgDropCallbackType, new DropCallbackMessage());
        };

        static readonly NetworkMessageDelegate msgEquipmentDropHandler = (NetworkMessage msg) =>
        {
            var equipmentIndex = msg.reader.ReadEquipmentIndex();
            ServerStuffDropper.DropEquipment(players[msg.conn], equipmentIndex);
        };

        static readonly NetworkMessageDelegate msgDropCallbackHandler = (NetworkMessage msg) =>
        {
            ConversionArtifactMod.master.GetComponent<InventoryLimits>().Count(ConversionArtifactMod.master.inventory);
        };

        static CharacterMaster master;
        
        public void Awake()
        {
            Conversion = ScriptableObject.CreateInstance<ArtifactDef>();
            Conversion.nameToken = "Artifact of Conversion";
            Conversion.descriptionToken = "Amount of your items is capped with your experience level, BUT you can DROP your items. Gain experience and share with friends! Click on an item to drop.";
            Sprite template = LoadoutAPI.CreateSkinIcon(Color.white, Color.white, Color.white, Color.white);
            string artifactIconPath = $"file:\\\\{Info.Location}\\..\\conv.png";
            string artifactIconDisabledPath = $"file:\\\\{Info.Location}\\..\\convdis.png";
            WWW w = new WWW(artifactIconPath);
            while (!w.isDone) ;
            WWW ww = new WWW(artifactIconDisabledPath);
            while (!ww.isDone) ;
            Texture2D artifactIconTexture = w.texture;
            Texture2D artifactIconDisabledTexture = ww.texture;
            Sprite artifactIcon = Sprite.Create(artifactIconTexture, template.rect, template.pivot);
            Sprite artifactDisabledIcon = Sprite.Create(artifactIconDisabledTexture, template.rect, template.pivot);
            Conversion.smallIconDeselectedSprite = artifactDisabledIcon;
            Conversion.smallIconSelectedSprite = artifactIcon;

            ArtifactCatalog.getAdditionalEntries += (list) =>
            {
                list.Add(Conversion);
            };

            RunArtifactManager.onArtifactEnabledGlobal += RunArtifactManager_onArtifactEnabledGlobal;

            RunArtifactManager.onArtifactDisabledGlobal += RunArtifactManager_onArtifactDisabledGlobal;
        }

        private void RunArtifactManager_onArtifactDisabledGlobal([JetBrains.Annotations.NotNull] RunArtifactManager runArtifactManager, [JetBrains.Annotations.NotNull] ArtifactDef artifactDef)
        {
            if (artifactDef.artifactIndex == Conversion.artifactIndex)
            {
                On.RoR2.GenericPickupController.AttemptGrant -= GenericPickupController_AttemptGrant;

                On.RoR2.GenericPickupController.OnTriggerStay -= GenericPickupController_OnTriggerStay;

                On.RoR2.Run.Start -= Run_Start;

                On.RoR2.PlayerCharacterMasterController.OnBodyStart += PlayerCharacterMasterController_OnBodyStart;

                On.RoR2.CharacterBody.OnLevelUp -= CharacterBody_OnLevelUp;

                On.RoR2.CharacterMaster.OnInventoryChanged -= CharacterMaster_OnInventoryChanged;

                On.RoR2.Run.OnUserAdded -= Run_OnUserAdded;

                On.RoR2.Run.OnUserRemoved -= Run_OnUserRemoved;

                players.Clear();

                if (NetworkServer.active)
                {
                    NetworkServer.UnregisterHandler(msgItemDropType);
                    NetworkServer.UnregisterHandler(msgEquipmentDropType);
                }
            }
        }

        private void RunArtifactManager_onArtifactEnabledGlobal([JetBrains.Annotations.NotNull] RunArtifactManager runArtifactManager, [JetBrains.Annotations.NotNull] ArtifactDef artifactDef)
        {
            if (artifactDef.artifactIndex == Conversion.artifactIndex)
            {
                On.RoR2.GenericPickupController.AttemptGrant += GenericPickupController_AttemptGrant;

                On.RoR2.GenericPickupController.OnTriggerStay += GenericPickupController_OnTriggerStay;

                On.RoR2.Run.Start += Run_Start;

                On.RoR2.PlayerCharacterMasterController.OnBodyStart += PlayerCharacterMasterController_OnBodyStart;

                On.RoR2.CharacterBody.OnLevelUp += CharacterBody_OnLevelUp;

                On.RoR2.CharacterMaster.OnInventoryChanged += CharacterMaster_OnInventoryChanged;

                On.RoR2.Run.OnUserAdded += Run_OnUserAdded;

                On.RoR2.Run.OnUserRemoved += Run_OnUserRemoved;

                if (players == null)
                    players = new Dictionary<NetworkConnection, CharacterMaster>();

                if (NetworkServer.active)
                {
                    NetworkServer.RegisterHandler(msgItemDropType, msgItemDropHandler);
                    NetworkServer.RegisterHandler(msgEquipmentDropType, msgEquipmentDropHandler);
                }

                FieldInfo chatMessageTypeToIndexField = typeof(ChatMessageBase).GetField("chatMessageTypeToIndex", BindingFlags.NonPublic | BindingFlags.Static);
                Dictionary<Type, byte> chatMessageTypeToIndex = (Dictionary<Type, byte>)chatMessageTypeToIndexField.GetValue(null);
                FieldInfo chatMessageIndexToTypeField = typeof(ChatMessageBase).GetField("chatMessageIndexToType", BindingFlags.NonPublic | BindingFlags.Static);
                List<Type> chatMessageIndexToType = (List<Type>)chatMessageIndexToTypeField.GetValue(null);

                foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
                {
                    if (type.IsSubclassOf(typeof(ChatMessageBase)) && !chatMessageTypeToIndex.ContainsKey(type) && !chatMessageIndexToType.Contains(type))
                    {
                        chatMessageTypeToIndex.Add(type, (byte)chatMessageIndexToType.Count);
                        chatMessageIndexToType.Add(type);
                    }
                }
                chatMessageTypeToIndexField.SetValue(null, chatMessageTypeToIndex);
                chatMessageIndexToTypeField.SetValue(null, chatMessageIndexToType);
            }
        }

        private void PlayerCharacterMasterController_OnBodyStart(On.RoR2.PlayerCharacterMasterController.orig_OnBodyStart orig, PlayerCharacterMasterController self)
        {
            if (NetworkClient.active)
            {
                ConversionArtifactMod.master = self.master;
                //self.networkUser.connectionToServer.RegisterHandler(msgDropCallbackType, msgDropCallbackHandler);
                //self.connectionToServer.RegisterHandler(msgDropCallbackType, msgDropCallbackHandler);
                //ClientScene.readyConnection.RegisterHandler(msgDropCallbackType, msgDropCallbackHandler);
            }
            var master = self.master;
            if (master != null && master.GetComponent<InventoryLimits>() == null && master.GetComponent<DropperApplier>() == null)
            {
                InventoryLimits limits = master.gameObject.AddComponent<InventoryLimits>();
                limits.limit = (int)TeamManager.instance.GetTeamLevel(self.master.teamIndex);
                limits.Count(master.inventory);
                DropperApplier dropperApplier = master.gameObject.AddComponent<DropperApplier>();
                dropperApplier.inventory = master.inventory;
                dropperApplier.master = master;
            }
            orig(self);
        }

        private void CharacterMaster_OnInventoryChanged(On.RoR2.CharacterMaster.orig_OnInventoryChanged orig, CharacterMaster self)
        {
            if (self.inventory != null)
            {
                var limits = self.GetComponent<InventoryLimits>();
                if (limits != null)
                {
                    limits.Count(self.inventory);
                }
            }
            orig(self);
        }

        private void CharacterBody_OnLevelUp(On.RoR2.CharacterBody.orig_OnLevelUp orig, CharacterBody self)
        {
            orig(self);
            if (self.master != null)
            {
                var limits = self.master.GetComponent<InventoryLimits>();
                if (limits != null)
                    limits.limit = (int)(TeamManager.instance.GetTeamLevel(self.master.teamIndex));
            }
        }

        private void Run_Start(On.RoR2.Run.orig_Start orig, Run self)
        {
            if (players == null)
                players = new Dictionary<NetworkConnection, CharacterMaster>();
            if (NetworkServer.active)
            {
                NetworkServer.RegisterHandler(msgItemDropType, msgItemDropHandler);
                NetworkServer.RegisterHandler(msgEquipmentDropType, msgEquipmentDropHandler);
            }
            orig(self);
        }

        private void Run_OnUserRemoved(On.RoR2.Run.orig_OnUserRemoved orig, Run self, NetworkUser user)
        {
            orig(self, user);
            players.Remove(user.connectionToClient);
        }

        private void Run_OnUserAdded(On.RoR2.Run.orig_OnUserAdded orig, Run self, NetworkUser user)
        {
            orig(self, user);
            if (user.connectionToClient != null && !players.ContainsKey(user.connectionToClient))
            {
                players.Add(user.connectionToClient, user.master);
            }
        }

        private void GenericPickupController_OnTriggerStay(On.RoR2.GenericPickupController.orig_OnTriggerStay orig, RoR2.GenericPickupController self, Collider other)
        {
            
        }

        private void GenericPickupController_AttemptGrant(On.RoR2.GenericPickupController.orig_AttemptGrant orig, RoR2.GenericPickupController self, RoR2.CharacterBody body)
        {
            if (PickupCatalog.GetPickupDef(self.pickupIndex).equipmentIndex != EquipmentIndex.None)
            {
                orig(self, body);
                return;
            }
            InventoryLimits limits = null;
            limits = body.master.gameObject.GetComponent<InventoryLimits>();
            if (limits != null && PickupCatalog.GetPickupDef(self.pickupIndex).itemIndex != ItemIndex.None)
            {
                if (limits.Limited)
                {
                    /*
                    Chat.AddMessage($"<color=#FFFF00>Inventory full: {limits.amount}/{limits.limit}. Gain experience to gain free space.</color>");
                    */
                    return;
                }
            }
            orig(self, body);
            if (limits != null && PickupCatalog.GetPickupDef(self.pickupIndex).itemIndex != ItemIndex.None)
            {
                if (NetworkServer.active)
                {
                    DropperChat.ItemCountMessage(body.GetUserName(), limits.amount, limits.limit);
                }
            }
        }
    }

    public class ArtifactDesignProvider : IResourceProvider
    {
        public string ModPrefix => throw new NotImplementedException();

        public UnityEngine.Object Load(string path, Type type)
        {
            throw new NotImplementedException();
        }

        public UnityEngine.Object[] LoadAll(Type type)
        {
            throw new NotImplementedException();
        }

        public ResourceRequest LoadAsync(string path, Type type)
        {
            throw new NotImplementedException();
        }
    }

    public class DropperApplier : MonoBehaviour
    {
        public Inventory inventory;
        public CharacterMaster master;
        public void Update()
        {
            foreach (var hud in HUD.readOnlyInstanceList)
            {
                if (hud == null && hud.itemInventoryDisplay == null) continue;
                List<ItemIcon> itemIcons = hud.itemInventoryDisplay.GetFieldValue<List<ItemIcon>>("itemIcons");
                foreach (var icon in itemIcons)
                {
                    if (icon.GetComponent<ItemDropper>() == null)
                    {
                        ItemDropper dropper = icon.gameObject.AddComponent<ItemDropper>();
                        dropper.master = master;
                    }
                }
                if (hud.equipmentIcons.Length > 0)
                {
                    EquipmentIcon equipmentIcon = hud.equipmentIcons[0];
                    if (equipmentIcon.targetInventory == inventory && equipmentIcon.GetComponent<EquipmentDropper>() == null)
                    {
                        EquipmentDropper dropper = equipmentIcon.gameObject.AddComponent<EquipmentDropper>();
                        dropper.master = master;
                    }
                }
            }
        }
    }

    public class DropCallbackMessage : MessageBase
    {
        public override void Serialize(NetworkWriter writer)
        {
            base.Serialize(writer);
        }
    }

    public class ItemDropMessage : MessageBase
    {
        ItemIndex itemIndex;
        public ItemDropMessage(ItemIndex itemIndex)
        {
            this.itemIndex = itemIndex;
        }
        public override void Serialize(NetworkWriter writer)
        {
            base.Serialize(writer);
            writer.Write(itemIndex);
        }
    }
    public class ItemDropper : MonoBehaviour, IPointerClickHandler
    {
        public CharacterMaster master;
        public void OnPointerClick(PointerEventData eventData)
        {
            if (master != null && master.playerCharacterMasterController != null)
            {
                ItemIcon itemIcon = GetComponent<ItemIcon>();
                ItemIndex itemIndex = itemIcon.GetFieldValue<ItemIndex>("itemIndex");
                if (ItemCatalog.GetItemDef(itemIndex).tier == ItemTier.NoTier)
                {
                    Debug.LogWarning("Trash cannot be dropped :D");
                    return;
                }
                if (NetworkServer.active)
                {
                    ServerStuffDropper.DropItem(master, itemIndex);
                    InventoryLimits limits = master.GetComponent<InventoryLimits>();
                    if (!limits) return;
                    limits.Count(master.inventory);
                }
                else
                {
                    if (ClientScene.readyConnection == null)
                    {
                        Debug.LogError("Connection is not ready.");
                    }
                    ClientScene.readyConnection.Send(ConversionArtifactMod.msgItemDropType, new ItemDropMessage(itemIndex));
                }
            }
        }
        
    }

    public class EquipmentDropMessage : MessageBase
    {
        EquipmentIndex equipmentIndex;
        public EquipmentDropMessage(EquipmentIndex equipmentIndex)
        {
            this.equipmentIndex = equipmentIndex;
        }
        public override void Serialize(NetworkWriter writer)
        {
            base.Serialize(writer);
            writer.Write(equipmentIndex);
        }
    }
    public class EquipmentDropper : MonoBehaviour, IPointerClickHandler
    {
        public CharacterMaster master;
        public void OnPointerClick(PointerEventData eventData)
        {
            if (master != null && master.playerCharacterMasterController != null)
            {
                EquipmentIcon equipmentIcon = GetComponent<EquipmentIcon>();
                if (!equipmentIcon.hasEquipment) return;
                EquipmentIndex equipmentIndex = equipmentIcon.targetEquipmentSlot.equipmentIndex;
                if (NetworkServer.active)
                {
                    ServerStuffDropper.DropEquipment(master, equipmentIndex);
                }
                else
                {
                    if (ClientScene.readyConnection == null)
                    {
                        Debug.LogError("Connection is not ready.");
                    }
                    ClientScene.readyConnection.Send(ConversionArtifactMod.msgEquipmentDropType, new EquipmentDropMessage(equipmentIndex));
                }
            }
        }
    }
    public class ServerStuffDropper : NetworkBehaviour
    {
        public static void DropItem(CharacterMaster master, ItemIndex itemIndex)
        {
            var transform = master.GetBodyObject().transform;
            var dropVector = UnityEngine.Random.insideUnitCircle;
            PickupDropletController.CreatePickupDroplet(PickupCatalog.FindPickupIndex(itemIndex), transform.position, new Vector3(dropVector.x, 0, dropVector.y) * 5f);
            master.inventory.RemoveItem(itemIndex);
            DropperChat.ItemDropMessage(master.GetBody().GetUserName(), itemIndex);
            var limits = master.GetComponent<InventoryLimits>();
            DropperChat.ItemCountMessage(master.GetBody().GetUserName(), limits.amount, limits.limit);
        }
        public static void DropEquipment(CharacterMaster master, EquipmentIndex equipmentIndex)
        {
            var transform = master.GetBodyObject().transform;
            var dropVector = UnityEngine.Random.insideUnitCircle;
            PickupDropletController.CreatePickupDroplet(PickupCatalog.FindPickupIndex(equipmentIndex), transform.position, new Vector3(dropVector.x, 0, dropVector.y) * 5f);
            master.inventory.SetEquipmentIndex(EquipmentIndex.None);
            DropperChat.EquipmentDropMessage(master.GetBody().GetUserName(), equipmentIndex);
        }
    }

    public static class DropperChat
    {
        public static void ItemDropMessage(string playerName, ItemIndex itemIndex)
        {
            Chat.SendBroadcastChat(new PlayerItemDropChatMessage() { playerName = playerName, itemIndex = itemIndex });
        }
        public static void EquipmentDropMessage(string playerName, EquipmentIndex equipmentIndex)
        {
            Chat.SendBroadcastChat(new PlayerEquipmentDropChatMessage() { playerName = playerName, equipmentIndex = equipmentIndex });
        }
        public static void ItemCountMessage(string playerName, int count, int maxCount)
        {
            Chat.SendBroadcastChat(new PlayerItemsCountChatMessage() { playerName = playerName, count = count , maxCount = maxCount });
        }
    }

    public class PlayerItemsCountChatMessage : ChatMessageBase
    {
        public string playerName;
        public int count;
        public int maxCount;
        public override string ConstructChatString()
        {
            return $"{playerName} has <color=#00FF00>{count}</color>/<color=#FFFF00>{maxCount}</color> items.";
        }

        public override void Deserialize(NetworkReader reader)
        {
            base.Deserialize(reader);
            playerName = reader.ReadString();
            count = reader.ReadInt32();
            maxCount = reader.ReadInt32();
        }

        public override void Serialize(NetworkWriter writer)
        {
            base.Serialize(writer);
            writer.Write(playerName);
            writer.Write(count);
            writer.Write(maxCount);
        }
    }
    public class PlayerItemDropChatMessage : ChatMessageBase
    {
        public string playerName;
        public ItemIndex itemIndex;
        public override string ConstructChatString()
        {
            ItemDef itemDef = ItemCatalog.GetItemDef(itemIndex);
            return $"{playerName} dropped <color=#{ColorCatalog.GetColorHexString(itemDef.colorIndex)}>{Language.GetString(itemDef.nameToken)}</color>.";
        }

        public override void Deserialize(NetworkReader reader)
        {
            base.Deserialize(reader);
            playerName = reader.ReadString();
            itemIndex = reader.ReadItemIndex();
        }

        public override void Serialize(NetworkWriter writer)
        {
            base.Serialize(writer);
            writer.Write(playerName);
            writer.Write(itemIndex);
        }
    }
    public class PlayerEquipmentDropChatMessage : ChatMessageBase
    {
        public string playerName;
        public EquipmentIndex equipmentIndex;
        public override string ConstructChatString()
        {
            EquipmentDef equipmentDef = EquipmentCatalog.GetEquipmentDef(equipmentIndex);
            return $"{playerName} dropped <color=#{ColorCatalog.GetColorHexString(equipmentDef.colorIndex)}>{Language.GetString(equipmentDef.nameToken)}</color>.";
        }

        public override void Deserialize(NetworkReader reader)
        {
            base.Deserialize(reader);
            playerName = reader.ReadString();
            equipmentIndex = reader.ReadEquipmentIndex();
        }

        public override void Serialize(NetworkWriter writer)
        {
            base.Serialize(writer);
            writer.Write(playerName);
            writer.Write(equipmentIndex);
        }
    }

    public class InventoryLimits : MonoBehaviour
    {
        public int limit;
        public int amount;
        public bool Limited => amount >= limit;

        public int whiteItemsAmount;
        public int greenItemsAmount;
        public int redItemsAmount;
        public int lunarItemsAmount;
        public int bossItemsAmount;

        public void Awake()
        {
            whiteItemsAmount = 0;
            greenItemsAmount = 0;
            redItemsAmount = 0;
            lunarItemsAmount = 0;
            bossItemsAmount = 0;
            amount = 0;
        }
        public void Count(Inventory inventory)
        {
            whiteItemsAmount = inventory.GetTotalItemCountOfTier(ItemTier.Tier1);
            greenItemsAmount = inventory.GetTotalItemCountOfTier(ItemTier.Tier2);
            redItemsAmount = inventory.GetTotalItemCountOfTier(ItemTier.Tier3);
            lunarItemsAmount = inventory.GetTotalItemCountOfTier(ItemTier.Lunar);
            bossItemsAmount = inventory.GetTotalItemCountOfTier(ItemTier.Boss);

            amount = whiteItemsAmount + greenItemsAmount + redItemsAmount + lunarItemsAmount + bossItemsAmount;
        }
        public static int GetAmount(Inventory inventory)
        {
            var whiteItemsAmount = inventory.GetTotalItemCountOfTier(ItemTier.Tier1);
            var greenItemsAmount = inventory.GetTotalItemCountOfTier(ItemTier.Tier2);
            var  redItemsAmount = inventory.GetTotalItemCountOfTier(ItemTier.Tier3);
            var lunarItemsAmount = inventory.GetTotalItemCountOfTier(ItemTier.Lunar);
            var bossItemsAmount = inventory.GetTotalItemCountOfTier(ItemTier.Boss);

            var amount = whiteItemsAmount + greenItemsAmount + redItemsAmount + lunarItemsAmount + bossItemsAmount;
            return amount;
        }
    }
}
