using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Public Lockers", "LowPopLabs", "0.2.0")]
    [Description("Rent a personal locker by the day and open it from a terminal at any monument: what goes in at one terminal is there at every other. Rent is paid at the Public Works office, or in scrap at the terminal when that plugin is not installed.")]
    public class PublicLockers : RustPlugin
    {
        [PluginReference] private Plugin PublicWorks;

        private const string PermAdmin = "publiclockers.admin";
        private const string PermUse = "publiclockers.use";
        private const string BillKey = "locker";
        private const string ScrapShortname = "scrap";
        private const double DaySeconds = 86400;

        // The box a player actually loots. It is never networked to anyone but its
        // owner, and only while they have it open.
        private const string StoragePrefab = "assets/prefabs/deployable/large wood storage/box.wooden.large.prefab";

        private static void SetFlagNet(BaseEntity entity, BaseEntity.Flags flag, bool value)
        {
            using (var scope = entity.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate))
            {
                scope.Set(flag, value);
            }
        }

        #region Lang

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Prefix"] = "<color=#f9a825>Lockers</color>: ",
                ["NoPermission"] = "You don't have permission to do that.",

                ["Bill.Label"] = "Public locker",
                ["Bill.Description"] = "Your locker at every monument terminal",
                ["Bill.NotRented"] = "NOT RENTED",
                ["Bill.Left"] = "{0} left",
                ["Bill.Locked"] = "LOCKED — rent due",
                ["Bill.Button"] = "+1 DAY",

                ["Status.NotRented"] = "You have no locker rented. Rent is {0} scrap a day.",
                ["Status.Active"] = "Your locker is paid for another {0}. It holds {1} item(s).",
                ["Status.Grace"] = "Your locker is locked: the rent ran out. Its {1} item(s) are kept for another {0}, then destroyed.",
                ["Status.Held"] = "Your locker is locked: the rent ran out. Its {0} item(s) are held until you pay.",
                ["Status.MaxPrepaid"] = "Rent can be paid up to {0} day(s) ahead.",

                ["Pay.Office"] = "Pay the rent ({0} scrap a day) at the Public Works office, under MY ACCOUNTS.",
                ["Pay.OfficeGrid"] = "Pay the rent ({0} scrap a day) at the Public Works office in {1}, under MY ACCOUNTS.",
                ["Pay.Phone"] = "Or call the office from any telephone: {0}.",
                ["Pay.Terminal"] = "Pay the rent ({0} scrap a day) with /locker rent while standing at a terminal.",
                ["Pay.NotHere"] = "Stand at a locker terminal to pay the rent.",
                ["Pay.AtOfficeOnly"] = "Rent is paid at the Public Works office, not here.",
                ["Pay.NeedScrap"] = "You need {0} scrap for {1} day(s) of rent.",
                ["Pay.Done"] = "Paid {0} scrap. Your locker is open for another {1}.",
                ["Pay.Extended"] = "Your locker is open for another {0}.",
                ["Pay.Usage"] = "Usage: /locker rent [days]",

                ["Store.Barred"] = "{0} can't be kept in a locker.",
                ["Wiped"] = "Your locker went unpaid past the grace period and its contents were destroyed.",

                ["Admin.Usage"] = "/locker add · place · remove · list · grant <player> <days>",
                ["Admin.Added"] = "Locker marked as a terminal at {0}. It applies to {1} copy(ies) of that monument on this map.",
                ["Admin.AddedWorld"] = "Terminal marked. It is not inside a monument, so it will not survive a map wipe.",
                ["Admin.LookAt"] = "Look at the locker you want to mark, from within 5 m, and run /locker add again.",
                ["Admin.AlreadyMarked"] = "That spot is already a terminal.",
                ["Admin.Placed"] = "Locker placed at {0}. It stands at {1} copy(ies) of that monument on this map.",
                ["Admin.PlacedWorld"] = "Locker placed. It is not inside a monument, so it will not survive a map wipe.",
                ["Admin.KindPlaced"] = "spawned locker",
                ["Admin.KindMarked"] = "marked spot",
                ["Admin.Removed"] = "Terminal removed ({0}).",
                ["Admin.NoneNear"] = "No terminal within {0} m.",
                ["Admin.ListHeader"] = "{0} spot(s), {1} terminal(s) in use on this map:",
                ["Admin.ListLine"] = "  {0} @ {1} ({2})",
                ["Admin.ListNone"] = "No terminals yet. Look at a locker in a monument and run /locker add, or stand where one should go and run /locker place.",
                ["Admin.NoPlayer"] = "No player found for '{0}'.",
                ["Admin.Granted"] = "{0} now has {1} of locker rent.",

                ["Help.Title"] = "Public Lockers",
                ["Help.Description"] = "Rent a locker by the day. Every locker terminal on the island opens the same locker, so what you leave at one monument is waiting at the next.",
                ["Help.HowTo"] = "Pay the rent at the Public Works office (MY ACCOUNTS), or with /locker rent at a terminal when there is no office.\nPress the use key on a locker terminal at a monument to reach your locker.\nKeep the rent paid: an unpaid locker locks, and its contents are only kept for the grace period.",
                ["Help.Notes"] = "The locker is yours alone; teammates can't open it.",
                ["Help.Locations"] = "Locker terminals on this map:",
                ["Help.LocationLine"] = "{0} — {1}",
                ["Help.LocationOther"] = "Elsewhere",
                ["Help.NoLocations"] = "No locker terminals are in service yet.",
                ["Help.Cmd.Status"] = "How long your locker is paid for",
                ["Help.Cmd.Rent"] = "Pay rent in scrap at a terminal (servers without a Public Works office)",
                ["Help.Cmd.Admin"] = "Admin: mark or place, remove and list terminals; grant rent",
            }, this);
        }

        private string Msg(string key, string userId, params object[] args)
        {
            string text = lang.GetMessage(key, this, userId);
            return args != null && args.Length > 0 ? string.Format(text, args) : text;
        }

        private void Reply(BasePlayer player, string key, params object[] args) =>
            SendReply(player, Msg("Prefix", player.UserIDString) + Msg(key, player.UserIDString, args));

        // Unprefixed reply (admin command feedback, follow-on lines).
        private void ReplyRaw(BasePlayer player, string key, params object[] args) =>
            SendReply(player, Msg(key, player.UserIDString, args));

        private static string FormatSpan(double seconds)
        {
            if (seconds < 60) return "1m";
            int minutes = (int)(seconds / 60);
            int days = minutes / 1440, hours = minutes % 1440 / 60, mins = minutes % 60;
            if (days > 0) return hours > 0 ? $"{days}d {hours}h" : $"{days}d";
            if (hours > 0) return mins > 0 ? $"{hours}h {mins}m" : $"{hours}h";
            return $"{mins}m";
        }

        #endregion

        #region Configuration

        private Configuration config;
        private bool configBroken;

        private class TerminalSpot
        {
            [JsonProperty("Monument (empty = a fixed world position)")]
            public string Monument = "";

            [JsonProperty("Position (relative to the monument)")]
            public string Position = "";

            [JsonProperty("Spawn a locker here (false = a marked spot on the monument's own scenery)")]
            public bool Placed = false;

            [JsonProperty("Rotation Y of the spawned locker (relative to the monument)")]
            public float RotationY;
        }

        private class Configuration
        {
            [JsonProperty("Rent per day (scrap)")]
            public int RentPerDay = 50;

            [JsonProperty("Locker size (slots, 1-48)")]
            public int Slots = 48;

            [JsonProperty("Maximum days of rent a player can hold in advance")]
            public int MaxPrepaidDays = 14;

            [JsonProperty("Grace period after the rent runs out (days the locked locker keeps its contents)")]
            public float GraceDays = 3f;

            [JsonProperty("Destroy the contents when the grace period ends (false = hold them until the rent is paid)")]
            public bool DestroyAfterGrace = false;

            [JsonProperty("Empty every locker on a map wipe")]
            public bool EmptyOnMapWipe = true;

            [JsonProperty("Items that can't be stored (shortnames, e.g. explosive.timed; empty = nothing is refused)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> BarredItems = new List<string>();

            [JsonProperty("Require permission to use lockers (publiclockers.use)")]
            public bool RequirePermission = false;

            [JsonProperty("Take rent in scrap at the terminal when PublicWorks is not loaded (/locker rent)")]
            public bool RentAtTerminal = true;

            [JsonProperty("Distance from a terminal that counts as standing at it (meters)")]
            public float TerminalRange = 4f;

            [JsonProperty("Reach when using a terminal (meters)")]
            public float UseDistance = 3f;

            [JsonProperty("How close to the marked spot the player must be looking (meters)")]
            public float MarkRadius = 1f;

            [JsonProperty("Prefab for lockers spawned with /locker place")]
            public string PlacedPrefab = "assets/prefabs/deployable/locker/locker.deployed.prefab";

            [JsonProperty("Terminals (marked with /locker add)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<TerminalSpot> Terminals = new List<TerminalSpot>();
        }

        protected override void LoadDefaultConfig() => config = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null) throw new Exception();
                configBroken = false;
            }
            catch
            {
                PrintError($"Config file is invalid JSON — running on defaults until it's fixed. " +
                           $"The file was left untouched: repair oxide/config/{Name}.json (check quotes and commas) and run oxide.reload {Name}.");
                LoadDefaultConfig();
                configBroken = true;
            }
            if (config.BarredItems == null) config.BarredItems = new List<string>();
            if (config.Terminals == null) config.Terminals = new List<TerminalSpot>();
            SaveConfig();

            barred.Clear();
            foreach (var shortname in config.BarredItems)
                if (!string.IsNullOrEmpty(shortname)) barred.Add(shortname.Trim().ToLower());
        }

        protected override void SaveConfig()
        {
            if (configBroken)
            {
                PrintWarning($"Not saving config — oxide/config/{Name}.json is invalid JSON and writing would overwrite it. Fix the file and reload the plugin.");
                return;
            }
            Config.WriteObject(config);
        }

        private readonly HashSet<string> barred = new HashSet<string>();

        private int Slots => Mathf.Clamp(config.Slots, 1, 48);
        private int Rent => Mathf.Max(0, config.RentPerDay);
        private double GraceSeconds => Math.Max(0, config.GraceDays) * DaySeconds;

        #endregion

        #region Data

        // One record per item, the same fields the game writes to its own save.
        private class SavedItem
        {
            [JsonProperty("id")] public int Id;
            [JsonProperty("n")] public int Amount = 1;
            [JsonProperty("slot")] public int Slot;
            [JsonProperty("skin", DefaultValueHandling = DefaultValueHandling.Ignore)] public ulong Skin;
            [JsonProperty("cond", DefaultValueHandling = DefaultValueHandling.Ignore)] public float Condition;
            [JsonProperty("maxcond", DefaultValueHandling = DefaultValueHandling.Ignore)] public float MaxCondition;
            [JsonProperty("flags", DefaultValueHandling = DefaultValueHandling.Ignore)] public int Flags;
            [JsonProperty("name", DefaultValueHandling = DefaultValueHandling.Ignore)] public string Name;
            [JsonProperty("text", DefaultValueHandling = DefaultValueHandling.Ignore)] public string Text;

            // loaded rounds / fuel, stored +1 so 0 means "not a weapon"
            [JsonProperty("ammo", DefaultValueHandling = DefaultValueHandling.Ignore)] public int Ammo;
            [JsonProperty("ammotype", DefaultValueHandling = DefaultValueHandling.Ignore)] public int AmmoType;

            [JsonProperty("data", DefaultValueHandling = DefaultValueHandling.Ignore)] public bool HasData;
            [JsonProperty("dint", DefaultValueHandling = DefaultValueHandling.Ignore)] public int DataInt;
            [JsonProperty("dfloat", DefaultValueHandling = DefaultValueHandling.Ignore)] public float DataFloat;
            [JsonProperty("bp", DefaultValueHandling = DefaultValueHandling.Ignore)] public int BlueprintTarget;
            [JsonProperty("bpn", DefaultValueHandling = DefaultValueHandling.Ignore)] public int BlueprintAmount;

            // the item's own container: attachments, armour inserts, liquid
            [JsonProperty("cslots", DefaultValueHandling = DefaultValueHandling.Ignore)] public int ContentsSlots;
            [JsonProperty("ctype", DefaultValueHandling = DefaultValueHandling.Ignore)] public int ContentsType;
            [JsonProperty("contents", DefaultValueHandling = DefaultValueHandling.Ignore)] public List<SavedItem> Contents;
        }

        private class LockerData
        {
            public double PaidUntil;   // epoch seconds; 0 = never rented

            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<SavedItem> Items = new List<SavedItem>();
        }

        private class StoredData
        {
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<ulong, LockerData> Lockers = new Dictionary<ulong, LockerData>();
        }

        private StoredData data;

        private void LoadData()
        {
            try { data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name); }
            catch (Exception e)
            {
                // Never start from an empty file over a damaged one: lockers would be lost on the next save.
                dataBroken = true;
                PrintError($"Data file oxide/data/{Name}.json could not be read ({e.Message}). Lockers are closed and nothing will be saved until it is repaired and the plugin reloaded.");
            }
            if (data == null) data = new StoredData();
            if (data.Lockers == null) data.Lockers = new Dictionary<ulong, LockerData>();
            foreach (var locker in data.Lockers.Values)
                if (locker.Items == null) locker.Items = new List<SavedItem>();
        }

        private bool dataBroken;

        // The file is written with the world save (and on unload), not on every change:
        // a crash then rolls lockers and player inventories back to the same moment,
        // so an item is never in both places or in neither.
        private void SaveData()
        {
            if (dataBroken) return;
            foreach (var session in sessions.Values)
                StoreSession(session);
            Interface.Oxide.DataFileSystem.WriteObject(Name, data);
        }

        private static double Now => DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

        private LockerData GetLocker(ulong userId, bool create = false)
        {
            LockerData locker;
            if (data.Lockers.TryGetValue(userId, out locker)) return locker;
            if (!create) return null;
            locker = new LockerData();
            data.Lockers[userId] = locker;
            return locker;
        }

        #endregion

        #region Rental

        private enum RentState { None, Active, Grace, Lapsed }

        private RentState StateOf(LockerData locker)
        {
            if (locker == null || locker.PaidUntil <= 0) return RentState.None;
            double now = Now;
            if (now < locker.PaidUntil) return RentState.Active;
            return now < locker.PaidUntil + GraceSeconds ? RentState.Grace : RentState.Lapsed;
        }

        // Past the grace period the contents go, if the server is set up that way.
        // Returns true when this call destroyed something.
        private bool Expire(ulong userId, LockerData locker)
        {
            if (locker == null || StateOf(locker) != RentState.Lapsed) return false;
            if (!config.DestroyAfterGrace)
                return false;
            if (sessions.ContainsKey(userId)) CloseSession(userId, false);
            bool hadItems = locker.Items.Count > 0;
            locker.Items.Clear();
            locker.PaidUntil = 0;
            return hadItems;
        }

        private void ExpireAll()
        {
            if (!config.DestroyAfterGrace) return;
            int wiped = 0;
            foreach (var pair in data.Lockers)
                if (Expire(pair.Key, pair.Value)) wiped++;
            if (wiped > 0) Puts($"{wiped} locker(s) went unpaid past the grace period; contents destroyed.");
        }

        private int CountItems(ulong userId, LockerData locker)
        {
            Session session;
            if (sessions.TryGetValue(userId, out session) && session.Container != null && !session.Container.IsDestroyed)
                return session.Container.inventory.itemList.Count + session.Unplaced.Count;
            return locker != null ? locker.Items.Count : 0;
        }

        // How many more whole days this player may buy right now.
        private int DaysPurchasable(LockerData locker)
        {
            double paidAhead = locker != null ? Math.Max(0, locker.PaidUntil - Now) : 0;
            return Mathf.Max(0, (int)Math.Floor((Math.Max(1, config.MaxPrepaidDays) * DaySeconds - paidAhead) / DaySeconds + 0.0001));
        }

        private double ExtendRental(ulong userId, double days)
        {
            var locker = GetLocker(userId, true);
            locker.PaidUntil = Math.Max(locker.PaidUntil, Now) + days * DaySeconds;
            return locker.PaidUntil - Now;
        }

        private bool MayUse(BasePlayer player) =>
            !config.RequirePermission || permission.UserHasPermission(player.UserIDString, PermUse);

        private bool OfficeOpen => PublicWorks != null && PublicWorks.IsLoaded;

        private void TellStatus(BasePlayer player)
        {
            var locker = GetLocker(player.userID);
            if (Expire(player.userID, locker)) Reply(player, "Wiped");

            switch (StateOf(locker))
            {
                case RentState.Active:
                    Reply(player, "Status.Active", FormatSpan(locker.PaidUntil - Now), CountItems(player.userID, locker));
                    return;
                case RentState.Grace:
                    if (config.DestroyAfterGrace)
                        Reply(player, "Status.Grace", FormatSpan(locker.PaidUntil + GraceSeconds - Now), locker.Items.Count);
                    else
                        Reply(player, "Status.Held", locker.Items.Count);
                    break;
                case RentState.Lapsed:
                    Reply(player, "Status.Held", locker.Items.Count);
                    break;
                default:
                    Reply(player, "Status.NotRented", Rent);
                    break;
            }
            TellWhereToPay(player);
        }

        private void TellWhereToPay(BasePlayer player)
        {
            if (OfficeOpen)
            {
                string grid = null;
                int phone = 0;
                try
                {
                    grid = PublicWorks.Call("GetOfficeGrid") as string;
                    object number = PublicWorks.Call("GetOfficePhoneNumber");
                    if (number != null) phone = Convert.ToInt32(number);
                }
                catch { }
                if (string.IsNullOrEmpty(grid)) ReplyRaw(player, "Pay.Office", Rent);
                else ReplyRaw(player, "Pay.OfficeGrid", Rent, grid);
                if (phone > 0) ReplyRaw(player, "Pay.Phone", phone);
            }
            else if (config.RentAtTerminal)
                ReplyRaw(player, "Pay.Terminal", Rent);
        }

        #endregion

        #region Hooks

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            permission.RegisterPermission(PermUse, this);
            LoadData();
            Unsubscribe(nameof(OnPlayerInput));   // only listened to while marked spots exist
            Unsubscribe(nameof(CanLootEntity));   // only while spawned lockers exist
            Unsubscribe(nameof(OnLockerSwap));
            Puts($"PublicLockers v{Version} loaded - by LowPopLabs - ko-fi.com/lowpoplabs");
        }

        private void OnNewSave(string filename)
        {
            if (data == null || dataBroken || !config.EmptyOnMapWipe) return;
            int emptied = 0;
            foreach (var locker in data.Lockers.Values)
            {
                if (locker.Items.Count > 0) emptied++;
                locker.Items.Clear();
            }
            SaveData();
            if (emptied > 0) Puts($"Map wipe: emptied {emptied} locker(s). Paid rent carries over.");
        }

        private void OnServerInitialized()
        {
            immortal = ScriptableObject.CreateInstance<ProtectionProperties>();
            immortal.name = "PublicLockersTerminalProtection";
            immortal.Add(1f);

            ResolveTerminals();
            ExpireAll();
            expireTimer = timer.Every(600f, ExpireAll);
            RegisterBill();
        }

        private void OnPublicWorksReady() => RegisterBill();   // PublicWorks (re)loaded

        private void OnServerSave() => SaveData();

        private void Unload()
        {
            expireTimer?.Destroy();
            foreach (var userId in new List<ulong>(sessions.Keys))
                CloseSession(userId, false);
            SaveData();
            KillPlaced();
            if (immortal != null) UnityEngine.Object.Destroy(immortal);
            if (OfficeOpen) PublicWorks.Call("UnregisterBillable", this, BillKey);
        }

        // A spawned locker is only a door: using one opens the player's own locker instead.
        private object CanLootEntity(BasePlayer player, StorageContainer container)
        {
            if (container == null || container.net == null || !placedIds.Contains(container.net.ID.Value)) return null;
            Vector3 position = container.transform.position;
            NextTick(() => UseTerminal(player, position));
            return false;
        }

        // The gear-swap buttons are never shown (the loot panel never opens), but refuse
        // the call anyway so nothing can be pushed into a spawned locker's own inventory.
        private object OnLockerSwap(Locker locker, int set, BasePlayer player)
        {
            if (locker == null || locker.net == null || !placedIds.Contains(locker.net.ID.Value)) return null;
            return false;
        }

        // A marked terminal is a locker that is part of the monument itself: scenery, not
        // an entity, so the use key is read directly and matched against the marked spots.
        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (input == null || !input.WasJustPressed(BUTTON.USE)) return;
            if (player == null || !player.userID.IsSteamId() || player.IsDead() || player.IsSleeping()) return;

            // cheap reject before any physics
            if (NearestTerminal(player.transform.position, config.UseDistance + config.MarkRadius + 2f) == null) return;

            float now = Time.realtimeSinceStartup;
            float last;
            if (lastUse.TryGetValue(player.userID, out last) && now - last < 0.5f) return;
            lastUse[player.userID] = now;

            RaycastHit hit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out hit, Mathf.Max(1f, config.UseDistance), Rust.Layers.Solid, QueryTriggerInteraction.Ignore))
                return;
            // Real entities (doors, boxes, etc.) handle their own interaction
            if (hit.GetEntity() != null) return;

            var terminal = NearestTerminal(hit.point, Mathf.Max(0.2f, config.MarkRadius));
            if (terminal != null && terminal.Entity == null) UseTerminal(player, terminal.Position);
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            lastUse.Remove(player.userID);
            barredNoticeAt.Remove(player.userID);
            CloseSession(player.userID, true);
        }

        private readonly Dictionary<ulong, float> lastUse = new Dictionary<ulong, float>();

        private void OnLootEntityEnd(BasePlayer player, BaseEntity entity)
        {
            if (entity == null || entity.net == null) return;
            ulong userId;
            if (sessionByContainer.TryGetValue(entity.net.ID.Value, out userId))
                CloseSession(userId, true);
        }

        private Timer expireTimer;

        #endregion

        #region Terminals

        // A spot resolved to a world position on this map. Entity is the locker the
        // plugin spawned there, or null for a spot marked on the monument's own scenery.
        private class LiveTerminal
        {
            public TerminalSpot Spot;
            public Vector3 Position;
            public string Place;   // monument name as the map shows it, for the help page
            public StorageContainer Entity;
        }

        private readonly List<LiveTerminal> terminals = new List<LiveTerminal>();
        private readonly HashSet<ulong> placedIds = new HashSet<ulong>();
        private ProtectionProperties immortal;

        // Some monuments (the lighthouse, for one) ship with zero-size bounds, so nothing
        // is ever "inside" them. A point that is in no monument's bounds belongs to the
        // nearest monument within this distance of its bounds instead.
        private const float MonumentReach = 40f;

        private MonumentInfo FindMonumentAt(Vector3 position)
        {
            if (TerrainMeta.Path == null || TerrainMeta.Path.Monuments == null) return null;
            MonumentInfo nearest = null;
            float best = MonumentReach;
            foreach (var monument in TerrainMeta.Path.Monuments)
            {
                if (monument == null) continue;
                if (monument.IsInBounds(position)) return monument;
                float distance = monument.Distance(position);
                if (distance <= best) { best = distance; nearest = monument; }
            }
            return nearest;
        }

        private static bool TryParsePosition(string text, out Vector3 position)
        {
            position = Vector3.zero;
            if (string.IsNullOrEmpty(text)) return false;
            var parts = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            float x, y, z;
            if (parts.Length != 3
                || !float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x)
                || !float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y)
                || !float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out z))
                return false;
            position = new Vector3(x, y, z);
            return true;
        }

        private static string FormatPosition(Vector3 v) => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F2} {1:F2} {2:F2}", v.x, v.y, v.z);

        // Store the spot relative to the containing monument so it survives map wipes
        // (the same monument gets a new world position each map).
        private TerminalSpot CaptureSpot(Vector3 world, float worldYaw = 0f)
        {
            var spot = new TerminalSpot();
            MonumentInfo monument = FindMonumentAt(world);
            if (monument != null)
            {
                spot.Monument = monument.name;
                spot.Position = FormatPosition(monument.transform.InverseTransformPoint(world));
                spot.RotationY = Mathf.Repeat(worldYaw - monument.transform.eulerAngles.y, 360f);
            }
            else
            {
                spot.Position = FormatPosition(world);
                spot.RotationY = Mathf.Repeat(worldYaw, 360f);
            }
            return spot;
        }

        // A spot inside a monument applies to every copy of that monument on the map.
        private List<MonumentInfo> AnchorsFor(TerminalSpot spot)
        {
            var anchors = new List<MonumentInfo>();
            if (TerrainMeta.Path == null || TerrainMeta.Path.Monuments == null) return anchors;
            foreach (var monument in TerrainMeta.Path.Monuments)
                if (monument != null && monument.name == spot.Monument) anchors.Add(monument);
            if (anchors.Count == 0)
            {
                // Fall back to a filename match in case the monument prefab variant differs this map.
                string fileName = spot.Monument.Substring(spot.Monument.LastIndexOf('/') + 1);
                foreach (var monument in TerrainMeta.Path.Monuments)
                    if (monument != null && monument.name.EndsWith(fileName)) anchors.Add(monument);
            }
            return anchors;
        }

        private int ResolveSpot(TerminalSpot spot)
        {
            Vector3 stored;
            if (!TryParsePosition(spot.Position, out stored))
            {
                PrintWarning($"Terminal has an unreadable position '{spot.Position}' — skipped.");
                return 0;
            }

            if (string.IsNullOrEmpty(spot.Monument))
            {
                AddTerminal(spot, stored, spot.RotationY, PlaceName(FindMonumentAt(stored), null));
                PrintWarning($"Terminal at {spot.Position} ({MapHelper.PositionToString(stored)}) is a fixed world position and will not survive a map wipe. Remove it and add it again to tie it to its monument.");
                return 1;
            }

            var anchors = AnchorsFor(spot);
            if (anchors.Count == 0)
                PrintWarning($"Terminal monument '{spot.Monument}' is not on this map — that terminal is out of use.");
            foreach (var anchor in anchors)
                AddTerminal(spot, anchor.transform.TransformPoint(stored), anchor.transform.eulerAngles.y + spot.RotationY, PlaceName(anchor, spot.Monument));
            return anchors.Count;
        }

        private void AddTerminal(TerminalSpot spot, Vector3 position, float yaw, string place)
        {
            var terminal = new LiveTerminal { Spot = spot, Position = position, Place = place };
            if (spot.Placed) terminal.Entity = SpawnPlaced(position, yaw);
            terminals.Add(terminal);
        }

        // The locker for a monument that has none of its own. Never saved, so it is
        // respawned on every load and gone on unload.
        private StorageContainer SpawnPlaced(Vector3 position, float yaw)
        {
            var entity = GameManager.server.CreateEntity(config.PlacedPrefab, position, Quaternion.Euler(0f, yaw, 0f));
            var locker = entity as StorageContainer;
            if (locker == null)
            {
                PrintWarning($"'{config.PlacedPrefab}' is not a storage container — no locker spawned. The spot still works with the use key.");
                if (entity != null) UnityEngine.Object.Destroy(entity.gameObject);
                return null;
            }

            locker.enableSaving = false;
            UnityEngine.Object.DestroyImmediate(locker.GetComponent<DestroyOnGroundMissing>());
            UnityEngine.Object.DestroyImmediate(locker.GetComponent<GroundWatch>());
            locker.Spawn();

            locker.baseProtection = immortal;
            locker.pickup.enabled = false;
            locker.dropsLoot = false;
            placedIds.Add(locker.net.ID.Value);
            return locker;
        }

        private static void KillEntity(LiveTerminal terminal)
        {
            if (terminal.Entity != null && !terminal.Entity.IsDestroyed) terminal.Entity.Kill();
            terminal.Entity = null;
        }

        private void KillPlaced()
        {
            foreach (var terminal in terminals)
                KillEntity(terminal);
            placedIds.Clear();
        }

        private void ResolveTerminals()
        {
            KillPlaced();
            terminals.Clear();
            foreach (var spot in config.Terminals)
                ResolveSpot(spot);
            if (config.Terminals.Count == 0)
                PrintWarning("No locker terminals yet — an admin should look at a locker in a monument and run /locker add, or stand where one should go and run /locker place");
            else
                Puts($"{terminals.Count} locker terminal(s) in use from {config.Terminals.Count} spot(s), {placedIds.Count} of them spawned lockers.");
            WatchInput();
        }

        // The name players know a monument by; the prefab file name when the map shows none.
        private static string PlaceName(MonumentInfo monument, string prefab)
        {
            string phrase = monument != null && monument.displayPhrase != null ? monument.displayPhrase.english : null;
            if (!string.IsNullOrEmpty(phrase)) return phrase.Trim();
            if (monument != null && string.IsNullOrEmpty(prefab)) prefab = monument.name;
            return string.IsNullOrEmpty(prefab) ? null : ShortMonument(prefab);
        }

        // The HelpMenu page lists the terminals, so it has to be rebuilt when they change.
        private void RefreshHelp()
        {
            if (plugins.Find("HelpMenu") != null)
                ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), "help.reload");
        }

        private void WatchInput()
        {
            bool marks = false;
            foreach (var terminal in terminals)
                if (terminal.Entity == null) { marks = true; break; }   // a failed spawn falls back to the use key
            if (marks) Subscribe(nameof(OnPlayerInput));
            else Unsubscribe(nameof(OnPlayerInput));

            if (placedIds.Count > 0)
            {
                Subscribe(nameof(CanLootEntity));
                Subscribe(nameof(OnLockerSwap));
            }
            else
            {
                Unsubscribe(nameof(CanLootEntity));
                Unsubscribe(nameof(OnLockerSwap));
            }
        }

        private LiveTerminal NearestTerminal(Vector3 position, float range)
        {
            LiveTerminal nearest = null;
            float best = range * range;
            foreach (var terminal in terminals)
            {
                float sqr = (terminal.Position - position).sqrMagnitude;
                if (sqr <= best) { best = sqr; nearest = terminal; }
            }
            return nearest;
        }

        private void UseTerminal(BasePlayer player, Vector3 position)
        {
            if (player == null || !player.IsConnected || player.IsDead()) return;
            if (dataBroken) return;
            if (!MayUse(player))
            {
                Reply(player, "NoPermission");
                return;
            }

            var locker = GetLocker(player.userID);
            if (Expire(player.userID, locker)) Reply(player, "Wiped");
            if (StateOf(locker) == RentState.Active) OpenLocker(player, position);
            else TellStatus(player);
        }

        #endregion

        #region Storage

        // An open locker: the live box, plus any saved records that could not be turned
        // back into items this time (they stay on file rather than being dropped).
        private class Session
        {
            public ulong Owner;
            public StorageContainer Container;
            public List<SavedItem> Unplaced = new List<SavedItem>();
        }

        private readonly Dictionary<ulong, Session> sessions = new Dictionary<ulong, Session>();
        private readonly Dictionary<ulong, ulong> sessionByContainer = new Dictionary<ulong, ulong>();
        private readonly Dictionary<ulong, float> barredNoticeAt = new Dictionary<ulong, float>();

        private void OpenLocker(BasePlayer player, Vector3 position)
        {
            CloseSession(player.userID, false);
            var locker = GetLocker(player.userID, true);

            var container = GameManager.server.CreateEntity(StoragePrefab, position, Quaternion.identity) as StorageContainer;
            if (container == null)
            {
                PrintWarning("Failed to create the locker container entity.");
                return;
            }

            container.enableSaving = false;
            // Out of every client's network view; snapshotted to the owner only.
            container.limitNetworking = true;
            UnityEngine.Object.DestroyImmediate(container.GetComponent<DestroyOnGroundMissing>());
            UnityEngine.Object.DestroyImmediate(container.GetComponent<GroundWatch>());
            container.Spawn();

            // Disabled stops the client drawing the box; no colliders so it never blocks anything.
            SetFlagNet(container, BaseEntity.Flags.Disabled, true);
            foreach (var collider in container.GetComponentsInChildren<Collider>())
                UnityEngine.Object.DestroyImmediate(collider);
            container.pickup.enabled = false;
            container.dropsLoot = false;

            // Never smaller than what is already inside, so lowering the slot count loses nothing.
            int capacity = Slots;
            foreach (var saved in locker.Items)
                if (saved.Slot + 1 > capacity) capacity = saved.Slot + 1;
            container.inventory.capacity = Mathf.Clamp(capacity, 1, 48);

            var session = new Session { Owner = player.userID, Container = container };
            foreach (var saved in locker.Items)
                Place(saved, container.inventory, session.Unplaced);

            var prefabFilter = container.inventory.canAcceptItem;
            container.inventory.canAcceptItem = (mover, item, slot) =>
                CanStore(mover, item) && (prefabFilter == null || prefabFilter(mover, item, slot));

            sessions[player.userID] = session;
            sessionByContainer[container.net.ID.Value] = player.userID;

            if (player.net?.connection != null)
                container.SendAsSnapshot(player.net.connection);
            if (!container.PlayerOpenLoot(player, container.panelName, doPositionChecks: false))
                CloseSession(player.userID, false);
        }

        // Copy the live box back into the record. The box stays as it is.
        private void StoreSession(Session session)
        {
            if (session.Container == null || session.Container.IsDestroyed) return;
            var locker = GetLocker(session.Owner, true);
            var items = new List<SavedItem>(session.Container.inventory.itemList.Count + session.Unplaced.Count);
            foreach (var item in session.Container.inventory.itemList)
                if (item != null && item.IsValid()) items.Add(Capture(item));
            items.AddRange(session.Unplaced);
            locker.Items = items;
        }

        // deferKill: called from inside the game's own loot-end path, which still
        // touches the box after the hook returns.
        private void CloseSession(ulong userId, bool deferKill)
        {
            Session session;
            if (!sessions.TryGetValue(userId, out session)) return;
            sessions.Remove(userId);

            var container = session.Container;
            if (container == null || container.IsDestroyed) return;
            if (container.net != null) sessionByContainer.Remove(container.net.ID.Value);
            StoreSession(session);

            if (!deferKill)
            {
                var player = BasePlayer.FindByID(userId);
                if (player != null && player.inventory != null && player.inventory.loot != null && player.inventory.loot.entitySource == container)
                    player.EndLooting();
            }

            // From here the record is the only copy: the live items go now, the box after.
            container.inventory.Clear();
            if (deferKill)
                NextTick(() => { if (container != null && !container.IsDestroyed) container.Kill(); });
            else
                container.Kill();
        }

        private bool IsBarred(Item item)
        {
            if (item == null) return true;
            if (barred.Contains(item.info.shortname)) return true;
            // Items tied to a world entity (photos, cassettes, pagers) can't be rebuilt from a record.
            if (item.instanceData != null && item.instanceData.subEntity.IsValid) return true;
            if (item.contents != null)
                foreach (var child in item.contents.itemList)
                    if (IsBarred(child)) return true;
            return false;
        }

        private bool CanStore(BasePlayer mover, Item item)
        {
            if (!IsBarred(item)) return true;
            if (mover != null && item != null)
            {
                float last;
                float now = Time.realtimeSinceStartup;
                if (!barredNoticeAt.TryGetValue(mover.userID, out last) || now - last > 2f)
                {
                    barredNoticeAt[mover.userID] = now;
                    Reply(mover, "Store.Barred", item.info.displayName.english);
                }
            }
            return false;
        }

        private static SavedItem Capture(Item item)
        {
            var saved = new SavedItem
            {
                Id = item.info.itemid,
                Amount = item.amount,
                Slot = item.position,
                Skin = item.skin,
                Flags = (int)item.flags,
                Name = string.IsNullOrEmpty(item.name) ? null : item.name,
                Text = string.IsNullOrEmpty(item.text) ? null : item.text,
            };

            if (item.hasCondition)
            {
                saved.Condition = item.condition;
                saved.MaxCondition = item.maxCondition;
            }

            var held = item.GetHeldEntity();
            var gun = held as BaseProjectile;
            if (gun != null && gun.primaryMagazine != null)
            {
                saved.Ammo = gun.primaryMagazine.contents + 1;
                if (gun.primaryMagazine.ammoType != null) saved.AmmoType = gun.primaryMagazine.ammoType.itemid;
            }
            else if (held is Chainsaw) saved.Ammo = ((Chainsaw)held).ammo + 1;
            else if (held is FlameThrower) saved.Ammo = ((FlameThrower)held).ammo + 1;

            if (item.instanceData != null)
            {
                saved.HasData = true;
                saved.DataInt = item.instanceData.dataInt;
                saved.DataFloat = item.instanceData.dataFloat;
                saved.BlueprintTarget = item.instanceData.blueprintTarget;
                saved.BlueprintAmount = item.instanceData.blueprintAmount;
            }

            if (item.contents != null)
            {
                saved.ContentsSlots = item.contents.capacity;
                saved.ContentsType = (int)item.contents.allowedContents;
                if (item.contents.itemList.Count > 0)
                {
                    saved.Contents = new List<SavedItem>(item.contents.itemList.Count);
                    foreach (var child in item.contents.itemList)
                        if (child != null && child.IsValid()) saved.Contents.Add(Capture(child));
                }
            }
            return saved;
        }

        // Rebuild an item from its record. Children that will not seat in the rebuilt
        // item are handed back in orphans rather than destroyed.
        private static Item Restore(SavedItem saved, List<SavedItem> orphans)
        {
            var item = ItemManager.CreateByItemID(saved.Id, Mathf.Max(1, saved.Amount), saved.Skin);
            if (item == null) return null;

            item.flags = (Item.Flag)saved.Flags;
            if (saved.Name != null) item.name = saved.Name;
            if (saved.Text != null) item.text = saved.Text;

            if (item.hasCondition && saved.MaxCondition > 0f)
            {
                item.maxCondition = saved.MaxCondition;
                item.condition = saved.Condition;
            }

            if (saved.HasData)
            {
                if (item.instanceData == null)
                {
                    item.instanceData = new ProtoBuf.Item.InstanceData();
                    item.instanceData.ShouldPool = false;
                }
                item.instanceData.dataInt = saved.DataInt;
                item.instanceData.dataFloat = saved.DataFloat;
                item.instanceData.blueprintTarget = saved.BlueprintTarget;
                item.instanceData.blueprintAmount = saved.BlueprintAmount;
            }

            if (saved.Ammo > 0)
            {
                var held = item.GetHeldEntity();
                var gun = held as BaseProjectile;
                if (gun != null && gun.primaryMagazine != null)
                {
                    var ammoType = saved.AmmoType != 0 ? ItemManager.FindItemDefinition(saved.AmmoType) : null;
                    if (ammoType != null) gun.primaryMagazine.ammoType = ammoType;
                    gun.primaryMagazine.contents = saved.Ammo - 1;
                }
                else if (held is Chainsaw) ((Chainsaw)held).ammo = saved.Ammo - 1;
                else if (held is FlameThrower) ((FlameThrower)held).ammo = saved.Ammo - 1;
            }

            if (saved.ContentsSlots > 0)
            {
                if (item.contents == null)
                {
                    item.contents = new ItemContainer();
                    item.contents.allowedContents = (ItemContainer.ContentsType)(saved.ContentsType == 0 ? 1 : saved.ContentsType);
                    item.contents.ServerInitialize(item, saved.ContentsSlots);
                    item.contents.GiveUID();
                }
                else if (item.contents.capacity < saved.ContentsSlots)
                    item.contents.capacity = saved.ContentsSlots;
            }

            if (saved.Contents != null)
                foreach (var childSaved in saved.Contents)
                {
                    var childOrphans = new List<SavedItem>();
                    var child = Restore(childSaved, childOrphans);
                    if (child == null || item.contents == null
                        || (!child.MoveToContainer(item.contents, childSaved.Slot, false) && !child.MoveToContainer(item.contents, -1, false)))
                    {
                        child?.Remove();
                        orphans.Add(childSaved);   // whole record, its own children included
                        continue;
                    }
                    orphans.AddRange(childOrphans);
                }

            item.MarkDirty();
            return item;
        }

        private static void Place(SavedItem saved, ItemContainer target, List<SavedItem> unplaced)
        {
            var orphans = new List<SavedItem>();
            var item = Restore(saved, orphans);
            if (item == null
                || (!item.MoveToContainer(target, saved.Slot, false) && !item.MoveToContainer(target, -1, false)))
            {
                item?.Remove();
                unplaced.Add(saved);   // the whole record stays on file; its orphans are still inside it
                return;
            }
            unplaced.AddRange(orphans);
        }

        #endregion

        #region Paying through PublicWorks

        private void RegisterBill()
        {
            if (OfficeOpen)
                PublicWorks.Call("RegisterBillable", this, BillKey);
        }

        // PublicWorks asks this each time it draws the accounts page for a player.
        private Dictionary<string, object> PublicWorksBillQuery(BasePlayer player, string key)
        {
            if (key != BillKey || player == null || dataBroken || !MayUse(player)) return null;

            var locker = GetLocker(player.userID);
            Expire(player.userID, locker);
            var state = StateOf(locker);
            string userId = player.UserIDString;

            string status;
            if (state == RentState.Active) status = Msg("Bill.Left", userId, FormatSpan(locker.PaidUntil - Now));
            else if (state == RentState.None) status = Msg("Bill.NotRented", userId);
            else status = Msg("Bill.Locked", userId);

            return new Dictionary<string, object>
            {
                ["Label"] = Msg("Bill.Label", userId),
                ["Description"] = Msg("Bill.Description", userId),
                ["Status"] = status,
                ["Price"] = DaysPurchasable(locker) > 0 ? Rent : 0,
                ["Button"] = Msg("Bill.Button", userId),
                ["Active"] = state == RentState.Active
            };
        }

        // PublicWorks has already taken the scrap. Returning false makes it refund.
        private object PublicWorksBillPaid(BasePlayer player, string key, int scrap)
        {
            if (key != BillKey || player == null || dataBroken || !MayUse(player)) return false;
            var locker = GetLocker(player.userID);
            Expire(player.userID, locker);
            if (scrap < Rent || DaysPurchasable(locker) < 1) return false;

            Reply(player, "Pay.Extended", FormatSpan(ExtendRental(player.userID, 1)));
            return true;
        }

        #endregion

        #region Commands

        [ChatCommand("locker")]
        private void CmdLocker(BasePlayer player, string command, string[] args)
        {
            string sub = args.Length > 0 ? args[0].ToLower() : "";
            switch (sub)
            {
                case "":
                case "status":
                    if (!MayUse(player)) { Reply(player, "NoPermission"); return; }
                    TellStatus(player);
                    return;
                case "rent":
                case "pay":
                    CmdRent(player, args);
                    return;
            }

            if (!permission.UserHasPermission(player.UserIDString, PermAdmin))
            {
                Reply(player, "NoPermission");
                return;
            }

            switch (sub)
            {
                case "add":
                {
                    RaycastHit hit;
                    if (!Physics.Raycast(player.eyes.HeadRay(), out hit, 5f, Rust.Layers.Solid, QueryTriggerInteraction.Ignore))
                    {
                        ReplyRaw(player, "Admin.LookAt");
                        return;
                    }
                    if (NearestTerminal(hit.point, Mathf.Max(0.2f, config.MarkRadius)) != null)
                    {
                        ReplyRaw(player, "Admin.AlreadyMarked");
                        return;
                    }
                    var spot = CaptureSpot(hit.point);
                    config.Terminals.Add(spot);
                    SaveConfig();
                    int copies = ResolveSpot(spot);
                    WatchInput();
                    RefreshHelp();
                    ShowMarks(player);
                    if (string.IsNullOrEmpty(spot.Monument)) ReplyRaw(player, "Admin.AddedWorld");
                    else ReplyRaw(player, "Admin.Added", ShortMonument(spot.Monument), copies);
                    return;
                }
                case "place":
                {
                    Vector3 here = player.transform.position;
                    if (NearestTerminal(here, 1f) != null)
                    {
                        ReplyRaw(player, "Admin.AlreadyMarked");
                        return;
                    }
                    // the locker stands where the admin stands, its doors facing where they look from
                    var spot = CaptureSpot(here, player.eyes.rotation.eulerAngles.y + 180f);
                    spot.Placed = true;
                    config.Terminals.Add(spot);
                    SaveConfig();
                    int copies = ResolveSpot(spot);
                    WatchInput();
                    RefreshHelp();
                    if (string.IsNullOrEmpty(spot.Monument)) ReplyRaw(player, "Admin.PlacedWorld");
                    else ReplyRaw(player, "Admin.Placed", ShortMonument(spot.Monument), copies);
                    return;
                }
                case "remove":
                {
                    const float range = 5f;
                    var nearest = NearestTerminal(player.transform.position, range);
                    if (nearest == null) { ReplyRaw(player, "Admin.NoneNear", range); return; }
                    var spot = nearest.Spot;
                    config.Terminals.Remove(spot);
                    SaveConfig();
                    foreach (var terminal in terminals)
                    {
                        if (terminal.Spot != spot || terminal.Entity == null) continue;
                        if (terminal.Entity.net != null) placedIds.Remove(terminal.Entity.net.ID.Value);
                        KillEntity(terminal);
                    }
                    terminals.RemoveAll(t => t.Spot == spot);
                    WatchInput();
                    RefreshHelp();
                    ReplyRaw(player, "Admin.Removed", string.IsNullOrEmpty(spot.Monument) ? spot.Position : ShortMonument(spot.Monument));
                    return;
                }
                case "list":
                {
                    if (config.Terminals.Count == 0) { ReplyRaw(player, "Admin.ListNone"); return; }
                    ReplyRaw(player, "Admin.ListHeader", config.Terminals.Count, terminals.Count);
                    foreach (var spot in config.Terminals)
                        ReplyRaw(player, "Admin.ListLine", string.IsNullOrEmpty(spot.Monument) ? "world" : ShortMonument(spot.Monument), spot.Position,
                            Msg(spot.Placed ? "Admin.KindPlaced" : "Admin.KindMarked", player.UserIDString));
                    ShowMarks(player);
                    return;
                }
                case "grant":
                {
                    float days;
                    if (args.Length < 3 || !float.TryParse(args[2], out days) || days <= 0f) { ReplyRaw(player, "Admin.Usage"); return; }
                    var target = BasePlayer.Find(args[1]);
                    if (target == null) { ReplyRaw(player, "Admin.NoPlayer", args[1]); return; }
                    ReplyRaw(player, "Admin.Granted", target.displayName, FormatSpan(ExtendRental(target.userID, days)));
                    return;
                }
                default:
                    ReplyRaw(player, "Admin.Usage");
                    return;
            }
        }

        // The standalone desk: scrap straight from the player's inventory at a terminal.
        private void CmdRent(BasePlayer player, string[] args)
        {
            if (!MayUse(player)) { Reply(player, "NoPermission"); return; }
            if (dataBroken) return;
            if (OfficeOpen) { Reply(player, "Pay.AtOfficeOnly"); TellWhereToPay(player); return; }
            if (!config.RentAtTerminal) { Reply(player, "NoPermission"); return; }
            if (NearestTerminal(player.transform.position, Mathf.Max(1f, config.TerminalRange)) == null)
            {
                Reply(player, "Pay.NotHere");
                return;
            }

            int days = 1;
            if (args.Length > 1 && (!int.TryParse(args[1], out days) || days < 1))
            {
                Reply(player, "Pay.Usage");
                return;
            }

            var locker = GetLocker(player.userID);
            if (Expire(player.userID, locker)) Reply(player, "Wiped");
            int allowed = DaysPurchasable(locker);
            if (allowed < 1)
            {
                Reply(player, "Status.MaxPrepaid", Mathf.Max(1, config.MaxPrepaidDays));
                return;
            }
            if (days > allowed) days = allowed;

            int total = Rent * days;
            var def = ItemManager.FindItemDefinition(ScrapShortname);
            if (def == null) return;
            if (total > 0)
            {
                if (player.inventory.GetAmount(def.itemid) < total)
                {
                    Reply(player, "Pay.NeedScrap", total, days);
                    return;
                }
                player.inventory.Take(null, def.itemid, total);
            }
            Reply(player, "Pay.Done", total, FormatSpan(ExtendRental(player.userID, days)));
        }

        // Draw the marked spots near an admin for a few seconds (debug draw needs the admin flag).
        private void ShowMarks(BasePlayer player)
        {
            if (!player.IsAdmin) return;
            foreach (var terminal in terminals)
                if (terminal.Entity == null && (terminal.Position - player.transform.position).sqrMagnitude < 50f * 50f)
                    player.SendConsoleCommand("ddraw.sphere", 15f, Color.green, terminal.Position, Mathf.Max(0.2f, config.MarkRadius));
        }

        private static string ShortMonument(string name)
        {
            int slash = name.LastIndexOf('/');
            string file = slash >= 0 ? name.Substring(slash + 1) : name;
            return file.EndsWith(".prefab") ? file.Substring(0, file.Length - 7) : file;
        }

        #endregion

        #region HelpMenu

        // object GetHelpInfo() — the HelpMenu plugin's entry (its hook schema).
        [HookMethod("GetHelpInfo")]
        private object GetHelpInfo()
        {
            var commands = new List<Dictionary<string, object>>
            {
                HelpCommand("/locker", "", Msg("Help.Cmd.Status", null), ""),
                HelpCommand("/locker", "rent [days]", Msg("Help.Cmd.Rent", null), ""),
                HelpCommand("/locker", "add | place | remove | list | grant <player> <days>", Msg("Help.Cmd.Admin", null), PermAdmin),
            };
            var notes = new List<string>(Msg("Help.Notes", null).Split('\n'));
            AddLocationNotes(notes);

            return new Dictionary<string, object>
            {
                ["Plugin"] = Name,
                ["Title"] = Msg("Help.Title", null),
                ["Description"] = Msg("Help.Description", null),
                ["Commands"] = commands,
                ["HowTo"] = Msg("Help.HowTo", null).Split('\n'),
                ["Notes"] = notes,
                ["Order"] = 32,   // sidebar: straight after Public Works
            };
        }

        // Where the terminals are on this map: one line per place, with the map grid of each.
        private void AddLocationNotes(List<string> notes)
        {
            if (terminals.Count == 0)
            {
                notes.Add(Msg("Help.NoLocations", null));
                return;
            }

            var places = new List<string>();
            var grids = new Dictionary<string, List<string>>();
            foreach (var terminal in terminals)
            {
                string place = terminal.Place ?? Msg("Help.LocationOther", null);
                List<string> list;
                if (!grids.TryGetValue(place, out list))
                {
                    list = new List<string>();
                    grids[place] = list;
                    places.Add(place);
                }
                string grid = MapHelper.PositionToString(terminal.Position);
                if (!list.Contains(grid)) list.Add(grid);
            }
            places.Sort(StringComparer.OrdinalIgnoreCase);

            notes.Add(Msg("Help.Locations", null));
            foreach (var place in places)
                notes.Add(Msg("Help.LocationLine", null, place, string.Join(", ", grids[place])));
        }

        private static Dictionary<string, object> HelpCommand(string command, string args, string description, string perm) =>
            new Dictionary<string, object>
            {
                ["Command"] = command,
                ["Args"] = args,
                ["Description"] = description,
                ["Permission"] = perm,
                ["AuthLevel"] = 0,
            };

        #endregion
    }
}
