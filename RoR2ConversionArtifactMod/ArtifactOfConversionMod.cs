using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BepInEx;
using RoR2;
using RoR2.UI;
using R2API;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.Networking.NetworkSystem;
using UnityEngine.Networking.PlayerConnection;
using UnityEngine.UI;
using R2API.Utils;
using System.Reflection;

namespace RoR2ConversionArtifactMod
{
    [R2APISubmoduleDependency("LoadoutAPI")]
    [BepInPlugin(modGuid, modName, modVersion)]
    public class ConversionArtifactMod : BaseUnityPlugin
    {
        public const string modGuid = "ConversionArtifactMod";
        public const string modName = "Artifact of Conversion mod";
        public const string modVersion = "0.0.1";
        
        ArtifactDef Conversion;

        Dictionary<NetworkConnection, CharacterMaster> players;
        static short msgItemDropType = 321;
        static short msgEquipmentDropType = 322;
        static short msgDropCallbackType = 320;

        static CharacterMaster master;
        
        public void Awake()
        {
            Conversion = ScriptableObject.CreateInstance<ArtifactDef>();
            Conversion.nameToken = "Artifact of Conversion";
            Conversion.descriptionToken = "Amount of your items is capped with your experience level, BUT you can DROP your items. Gain experience and share with friends! Click on an item to drop.";
            Conversion.smallIconDeselectedSprite = LoadoutAPI.CreateSkinIcon(Color.white, Color.white, Color.white, Color.white);
            Conversion.smallIconSelectedSprite = LoadoutAPI.CreateSkinIcon(Color.gray, Color.white, Color.white, Color.white);

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

                On.RoR2.PlayerCharacterMasterController.Start -= PlayerCharacterMasterController_Start;

                On.RoR2.CharacterBody.OnLevelChanged -= CharacterBody_OnLevelChanged;

                On.RoR2.CharacterMaster.OnInventoryChanged -= CharacterMaster_OnInventoryChanged;

                On.RoR2.Run.OnUserAdded -= Run_OnUserAdded;

                On.RoR2.Run.OnUserRemoved -= Run_OnUserRemoved;
            }
        }

        private void RunArtifactManager_onArtifactEnabledGlobal([JetBrains.Annotations.NotNull] RunArtifactManager runArtifactManager, [JetBrains.Annotations.NotNull] ArtifactDef artifactDef)
        {
            if (artifactDef.artifactIndex == Conversion.artifactIndex)
            {
                On.RoR2.GenericPickupController.AttemptGrant += GenericPickupController_AttemptGrant;

                On.RoR2.GenericPickupController.OnTriggerStay += GenericPickupController_OnTriggerStay;

                On.RoR2.Run.Start += Run_Start;

                On.RoR2.PlayerCharacterMasterController.Start += PlayerCharacterMasterController_Start;

                On.RoR2.CharacterBody.OnLevelChanged += CharacterBody_OnLevelChanged;

                On.RoR2.CharacterMaster.OnInventoryChanged += CharacterMaster_OnInventoryChanged;

                On.RoR2.Run.OnUserAdded += Run_OnUserAdded;

                On.RoR2.Run.OnUserRemoved += Run_OnUserRemoved;

                if (NetworkServer.active)
                {
                    if (players == null)
                        players = new Dictionary<NetworkConnection, CharacterMaster>();
                    NetworkMessageDelegate msgItemDropHandler = (NetworkMessage msg) =>
                    {
                        var itemIndex = msg.reader.ReadItemIndex();
                        ServerStuffDropper.DropItem(players[msg.conn], itemIndex);
                        var limits = players[msg.conn].GetComponent<InventoryLimits>();
                        limits.Count(players[msg.conn].inventory);
                        msg.conn.Send(msgDropCallbackType, new DropCallbackMessage());
                    };
                    NetworkServer.RegisterHandler(msgItemDropType, msgItemDropHandler);

                    NetworkMessageDelegate msgEquipmentDropHandler = (NetworkMessage msg) =>
                    {
                        var equipmentIndex = msg.reader.ReadEquipmentIndex();
                        ServerStuffDropper.DropEquipment(players[msg.conn], equipmentIndex);
                    };
                    NetworkServer.RegisterHandler(msgEquipmentDropType, msgEquipmentDropHandler);
                }

                FieldInfo chatMessageTypeToIndexField = typeof(Chat.ChatMessageBase).GetField("chatMessageTypeToIndex", BindingFlags.NonPublic | BindingFlags.Static);
                Dictionary<Type, byte> chatMessageTypeToIndex = (Dictionary<Type, byte>)chatMessageTypeToIndexField.GetValue(null);
                FieldInfo chatMessageIndexToTypeField = typeof(Chat.ChatMessageBase).GetField("chatMessageIndexToType", BindingFlags.NonPublic | BindingFlags.Static);
                List<Type> chatMessageIndexToType = (List<Type>)chatMessageIndexToTypeField.GetValue(null);

                foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
                {
                    if (type.IsSubclassOf(typeof(Chat.ChatMessageBase)))
                    {
                        chatMessageTypeToIndex.Add(type, (byte)chatMessageIndexToType.Count);
                        chatMessageIndexToType.Add(type);
                    }
                }
                chatMessageTypeToIndexField.SetValue(null, chatMessageTypeToIndex);
                chatMessageIndexToTypeField.SetValue(null, chatMessageIndexToType);
            }
        }

        private void PlayerCharacterMasterController_Start(On.RoR2.PlayerCharacterMasterController.orig_Start orig, PlayerCharacterMasterController self)
        {
            if (NetworkClient.active)
            {
                ConversionArtifactMod.master = self.master;
                NetworkMessageDelegate msgDropCallbackHandler = (NetworkMessage msg) =>
                {
                    ConversionArtifactMod.master.GetComponent<InventoryLimits>().Count(ConversionArtifactMod.master.inventory);
                };
                ClientScene.readyConnection.RegisterHandler(msgDropCallbackType, msgDropCallbackHandler);
            }
            var master = self.master;
            if (master != null && master.GetComponent<InventoryLimits>() == null && master.GetComponent<DropperApplier>() == null)
            {
                InventoryLimits limits = master.gameObject.AddComponent<InventoryLimits>();
                limits.limit = (int)TeamManager.instance.GetTeamLevel(self.master.teamIndex);
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

        private void CharacterBody_OnLevelChanged(On.RoR2.CharacterBody.orig_OnLevelChanged orig, CharacterBody self)
        {
            orig(self);
            if (self.master != null)
            {
                var limits = self.master.GetComponent<InventoryLimits>();
                if (limits != null)
                    limits.limit = (int)(TeamManager.instance.GetTeamLevel(self.master.teamIndex) + 1);
            }
        }

        private void Run_Start(On.RoR2.Run.orig_Start orig, Run self)
        {
            if (NetworkServer.active)
            {
                if (players == null)
                    players = new Dictionary<NetworkConnection, CharacterMaster>();
                NetworkMessageDelegate msgItemDropHandler = (NetworkMessage msg) =>
                {
                    var itemIndex = msg.reader.ReadItemIndex();
                    ServerStuffDropper.DropItem(players[msg.conn], itemIndex);
                    var limits = players[msg.conn].GetComponent<InventoryLimits>();
                    limits.Count(players[msg.conn].inventory);
                    msg.conn.Send(msgDropCallbackType, new DropCallbackMessage());
                };
                NetworkServer.RegisterHandler(msgItemDropType, msgItemDropHandler);

                NetworkMessageDelegate msgEquipmentDropHandler = (NetworkMessage msg) =>
                {
                    var equipmentIndex = msg.reader.ReadEquipmentIndex();
                    ServerStuffDropper.DropEquipment(players[msg.conn], equipmentIndex);
                };
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
            players.Add(user.connectionToClient, user.master);
        }

        private void GenericPickupController_OnTriggerStay(On.RoR2.GenericPickupController.orig_OnTriggerStay orig, RoR2.GenericPickupController self, Collider other)
        { }

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
                    ClientScene.readyConnection.Send(321, new ItemDropMessage(itemIndex));
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
                    ClientScene.readyConnection.Send(322, new EquipmentDropMessage(equipmentIndex));
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

    public class PlayerItemsCountChatMessage : Chat.ChatMessageBase
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
    public class PlayerItemDropChatMessage : Chat.ChatMessageBase
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
    public class PlayerEquipmentDropChatMessage : Chat.ChatMessageBase
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
