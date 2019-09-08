﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Intersect.Enums;
using Intersect.GameObjects;
using Intersect.GameObjects.Events;
using Intersect.GameObjects.Maps;
using Intersect.Network.Packets.Server;
using Intersect.Server.Database.PlayerData;
using Intersect.Server.Database.PlayerData.Players;
using Intersect.Server.Database.PlayerData.Security;
using Intersect.Server.General;
using Intersect.Server.Localization;
using Intersect.Server.Maps;
using Intersect.Server.Networking;
using Intersect.Utilities;

using JetBrains.Annotations;

using Newtonsoft.Json;

namespace Intersect.Server.Entities
{
    using DbInterface = DbInterface;

    public partial class EntityInstance : IDisposable
    {
        [Column(Order = 1), JsonProperty(Order = -2)]
        public string Name { get; set; }
        public Guid MapId { get; set; }

        [JsonIgnore]
        [NotMapped]
        public MapInstance Map => MapInstance.Get(MapId);

        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public int Dir { get; set; }
        public string Sprite { get; set; }
        public string Face { get; set; }
        public int Level { get; set; }

        [JsonIgnore, Column("Vitals")]
        public string VitalsJson
        {
            get => DatabaseUtils.SaveIntArray(_vital, (int)Enums.Vitals.VitalCount);
            set => _vital = DatabaseUtils.LoadIntArray(value, (int)Enums.Vitals.VitalCount);
        }
        [JsonProperty("Vitals"), NotMapped]
        public int[] _vital { get; set; } = new int[(int)Enums.Vitals.VitalCount];
        [JsonProperty("MaxVitals"), NotMapped]
        private int[] _maxVital = new int[(int)Vitals.VitalCount];

        //Stats based on npc settings, class settings, etc for quick calculations
        [JsonIgnore, Column("BaseStats")]
        public string StatsJson
        {
            get => DatabaseUtils.SaveIntArray(BaseStats, (int)Enums.Stats.StatCount);
            set => BaseStats = DatabaseUtils.LoadIntArray(value, (int)Enums.Stats.StatCount);
        }
        [NotMapped]
        public int[] BaseStats { get; set; } = new int[(int)Enums.Stats.StatCount]; // TODO: Why can this be BaseStats while Vitals is _vital and MaxVitals is _maxVital?

        [JsonIgnore, Column("StatPointAllocations")]
        public string StatPointsJson
        {
            get => DatabaseUtils.SaveIntArray(StatPointAllocations, (int)Enums.Stats.StatCount);
            set => StatPointAllocations = DatabaseUtils.LoadIntArray(value, (int)Enums.Stats.StatCount);
        }

        [NotMapped]
        public int[] StatPointAllocations { get; set; } = new int[(int)Enums.Stats.StatCount];

        //Inventory
        [NotNull, JsonIgnore]
        public virtual List<InventorySlot> Items { get; set; } = new List<InventorySlot>();

        //Spells
        [NotNull, JsonIgnore]
        public virtual List<SpellSlot> Spells { get; set; } = new List<SpellSlot>();

        [NotMapped, JsonIgnore]
        public EntityStat[] Stat = new EntityStat[(int)Stats.StatCount];

        [JsonIgnore, Column("NameColor")]
        public string NameColorJson
        {
            get => DatabaseUtils.SaveColor(NameColor);
            set => NameColor = DatabaseUtils.LoadColor(value);
        }

        [NotMapped]
        public Color NameColor { get; set; }

        //Instance Values
        private Guid _id;
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column(Order = 0)]
        public Guid Id { get => GetId(); set => _id = value; }
        [NotMapped] public bool Dead { get; set; }

        //Combat
        [NotMapped, JsonIgnore] public long CastTime { get; set; }
        [NotMapped, JsonIgnore] public long AttackTimer { get; set; }
        [NotMapped, JsonIgnore] public bool Blocking { get; set; }
        [NotMapped, JsonIgnore] public EntityInstance CastTarget { get; set; }
        [NotMapped, JsonIgnore] public Guid CollisionIndex { get; set; }
        [NotMapped, JsonIgnore] public long CombatTimer { get; set; }

        //Visuals
        [NotMapped, JsonIgnore] public bool HideName { get; set; }
        [NotMapped, JsonIgnore] public bool HideEntity { get; set; } = false;
        [NotMapped, JsonIgnore] public List<Guid> Animations { get; set; } = new List<Guid>();

        //DoT/HoT Spells
        [NotMapped, JsonIgnore] public List<DoTInstance> DoT { get; set; } = new List<DoTInstance>();
        [NotMapped, JsonIgnore] public EventMoveRoute MoveRoute { get; set; } = null;
        [NotMapped, JsonIgnore] public EventPageInstance MoveRouteSetter { get; set; } = null;
        [NotMapped, JsonIgnore] public long MoveTimer { get; set; }
        [NotMapped, JsonIgnore] public bool Passable { get; set; } = false;
        [NotMapped, JsonIgnore] public long RegenTimer { get; set; } = Globals.Timing.TimeMs;
        [NotMapped, JsonIgnore] public int SpellCastSlot { get; set; } = 0;

        //Status effects
        [NotMapped, JsonIgnore] public Dictionary<SpellBase, StatusInstance> Statuses = new Dictionary<SpellBase, StatusInstance>();

        [NotMapped, JsonIgnore] public EntityInstance Target = null;

        [NotMapped, JsonIgnore] public bool IsDisposed { get; protected set; }

        public EntityInstance() : this(Guid.NewGuid())
        {

        }

        public virtual Guid GetId()
        {
            return _id;
        }

        //Initialization
        public EntityInstance(Guid instanceId)
        {
			for (var i = 0; i < (int)Stats.StatCount; i++)
            {
                Stat[i] = new EntityStat((Stats)i, this);
            }
            Id = instanceId;
        }

        public virtual void Dispose()
        {
            if (!IsDisposed)
            {
                IsDisposed = true;
            }
        }

        public virtual void Update(long timeMs)
        {
            //Cast timers
            if (CastTime != 0 && CastTime < timeMs)
            {
                CastTime = 0;
                CastSpell(Spells[SpellCastSlot].SpellId, SpellCastSlot);
                CastTarget = null;
            }
            //DoT/HoT timers
            for (var i = 0; i < DoT.Count; i++)
            {
                DoT[i].Tick();
            }
            for (var i = 0; i < (int)Stats.StatCount; i++)
            {
                if (Stat[i].Update())
                {
                    SendStatUpdate(i);
                }
            }
            //Regen Timers
            if (Globals.Timing.TimeMs > CombatTimer && Globals.Timing.TimeMs > RegenTimer)
            {
                ProcessRegen();
                RegenTimer = Globals.Timing.TimeMs + Options.RegenTime;
            }
            //Status timers
            var statusArray = Statuses.ToArray();
            foreach (var status in statusArray)
            {
                status.Value.TryRemoveStatus();
            }
            //If there is a removal of a status, update it client sided.
            if (Statuses.Count != statusArray.Count())
            {
                PacketSender.SendEntityVitals(this);
            }
        }

        //Movement
        /// <summary>
        ///     Determines if this entity can move in the direction given.
        ///     Returns -5 if the tile is completely out of bounds.
        ///     Returns -3 if a tile is blocked because of a Z dimension tile
        ///     Returns -2 if a tile is blocked by a map attribute.
        ///     Returns -1 for clear.
        ///     Returns the type of entity that is blocking the way (if one exists)
        /// </summary>
        /// <param name="moveDir"></param>
        /// <returns></returns>
        public virtual int CanMove(int moveDir)
        {
            var xOffset = 0;
            var yOffset = 0;
            //if (MoveTimer > Globals.System.GetTimeMs()) return -5;
            var tile = new TileHelper(MapId, X, Y);
            switch (moveDir)
            {
                case 0: //Up
                    yOffset--;
                    break;
                case 1: //Down
                    yOffset++;
                    break;
                case 2: //Left
                    xOffset--;
                    break;
                case 3: //Right
                    xOffset++;
                    break;
                case 4: //NW
                    yOffset--;
                    xOffset--;
                    break;
                case 5: //NE
                    yOffset--;
                    xOffset++;
                    break;
                case 6: //SW
                    yOffset++;
                    xOffset--;
                    break;
                case 7: //SE
                    yOffset++;
                    xOffset++;
                    break;
            }

            if (tile.Translate(xOffset, yOffset))
            {
                var tileAttribute = MapInstance.Get(tile.GetMapId())
                    .Attributes[tile.GetX(), tile.GetY()];
                if (tileAttribute != null)
                {
                    if (tileAttribute.Type == MapAttributes.Blocked) return -2;
                    if (tileAttribute.Type == MapAttributes.NpcAvoid && GetType() == typeof(Npc)) return -2;
                    if (tileAttribute.Type == MapAttributes.ZDimension && ((MapZDimensionAttribute)tileAttribute).BlockedLevel > 0 && ((MapZDimensionAttribute)tileAttribute).BlockedLevel - 1 == Z) return -3;
                    if (tileAttribute.Type == MapAttributes.Slide)
                    {
                        if (this.GetType() == typeof(EventPageInstance)) return -4;
                        switch (((MapSlideAttribute)tileAttribute).Direction)
                        {
                            case 1:
                                if (moveDir == 1) return -4;
                                break;
                            case 2:
                                if (moveDir == 0) return -4;
                                break;
                            case 3:
                                if (moveDir == 3) return -4;
                                break;
                            case 4:
                                if (moveDir == 2) return -4;
                                break;
                        }
                    }
                }
            }
            else
            {
                return -5; //Out of Bounds
            }

            if (!Passable)
            {
                var targetMap = MapInstance.Get(tile.GetMapId());
                var mapEntities = MapInstance.Get(tile.GetMapId()).GetEntities();
                for (var i = 0; i < mapEntities.Count; i++)
                {
                    var en = mapEntities[i];
                    if (en != null && en.X == tile.GetX() && en.Y == tile.GetY() && en.Z == Z && !en.Passable)
                    {
                        //Set a target if a projectile
                        CollisionIndex = en.Id;
                        if (en.GetType() == typeof(Player))
                        {
                            if (this.GetType() == typeof(Player))
                            {
                                //Check if this target player is passable....
                                if (!Options.Instance.Passability.Passable[(int)targetMap.ZoneType])
                                {
                                    return (int)EntityTypes.Player;
                                }
                            }
                            else
                            {
                                return (int)EntityTypes.Player;
                            }
                        }
                        else if (en.GetType() == typeof(Npc))
                        {
                            return (int)EntityTypes.Player;
                        }
                        else if (en.GetType() == typeof(Resource))
                        {
                            //If determine if we should walk
                            var res = ((Resource)en);
                            if ((!res.IsDead() && !res.Base.WalkableBefore) || (res.IsDead() && !res.Base.WalkableAfter))
                            {
                                return (int)EntityTypes.Resource;
                            }
                        }
                    }
                }
                //If this is an npc or other event.. if any global page exists that isn't passable then don't walk here!
                if (this.GetType() != typeof(Player))
                {
                    foreach (var evt in MapInstance.Get(tile.GetMapId()).GlobalEventInstances)
                    {
                        foreach (var en in evt.Value.GlobalPageInstance)
                        {
                            if (en != null && en.X == tile.GetX() && en.Y == tile.GetY() && en.Z == Z && !en.Passable)
                            {
                                return (int)EntityTypes.Event;
                            }
                        }
                    }
                }
            }

            return IsTileWalkable(tile.GetMap(),tile.GetX(),tile.GetY(),Z);
        }

        protected virtual int IsTileWalkable(MapInstance map, int x, int y, int z)
        {
            //Out of bounds if no map
            if (map == null) return -5;
            //Otherwise fine
            return -1;
        }

        protected virtual bool ProcessMoveRoute(Client client, long timeMs)
        {
            var moved = false;
            byte lookDir = 0, moveDir = 0;
            if (MoveRoute.ActionIndex < MoveRoute.Actions.Count)
            {
                switch (MoveRoute.Actions[MoveRoute.ActionIndex].Type)
                {
                    case MoveRouteEnum.MoveUp:
                        if (CanMove((int)Directions.Up) == -1)
                        {
                            Move((int)Directions.Up, client, false, true);
                            moved = true;
                        }
                        break;
                    case MoveRouteEnum.MoveDown:
                        if (CanMove((int)Directions.Down) == -1)
                        {
                            Move((int)Directions.Down, client, false, true);
                            moved = true;
                        }
                        break;
                    case MoveRouteEnum.MoveLeft:
                        if (CanMove((int)Directions.Left) == -1)
                        {
                            Move((int)Directions.Left, client, false, true);
                            moved = true;
                        }
                        break;
                    case MoveRouteEnum.MoveRight:
                        if (CanMove((int)Directions.Right) == -1)
                        {
                            Move((int)Directions.Right, client, false, true);
                            moved = true;
                        }
                        break;
                    case MoveRouteEnum.MoveRandomly:
                        var dir = (byte)Globals.Rand.Next(0, 4);
                        if (CanMove(dir) == -1)
                        {
                            Move(dir, client);
                            moved = true;
                        }
                        break;
                    case MoveRouteEnum.StepForward:
                        if (CanMove(Dir) > -1)
                        {
                            Move((byte)Dir, client);
                            moved = true;
                        }
                        break;
                    case MoveRouteEnum.StepBack:
                        switch (Dir)
                        {
                            case (int)Directions.Up:
                                moveDir = (int)Directions.Down;
                                break;
                            case (int)Directions.Down:
                                moveDir = (int)Directions.Up;
                                break;
                            case (int)Directions.Left:
                                moveDir = (int)Directions.Right;
                                break;
                            case (int)Directions.Right:
                                moveDir = (int)Directions.Left;
                                break;
                        }
                        if (CanMove(moveDir) > -1)
                        {
                            Move(moveDir, client);
                            moved = true;
                        }
                        break;
                    case MoveRouteEnum.FaceUp:
                        ChangeDir((int)Directions.Up);
                        moved = true;
                        break;
                    case MoveRouteEnum.FaceDown:
                        ChangeDir((int)Directions.Down);
                        moved = true;
                        break;
                    case MoveRouteEnum.FaceLeft:
                        ChangeDir((int)Directions.Left);
                        moved = true;
                        break;
                    case MoveRouteEnum.FaceRight:
                        ChangeDir((int)Directions.Right);
                        moved = true;
                        break;
                    case MoveRouteEnum.Turn90Clockwise:
                        switch (Dir)
                        {
                            case (int)Directions.Up:
                                lookDir = (int)Directions.Right;
                                break;
                            case (int)Directions.Down:
                                lookDir = (int)Directions.Left;
                                break;
                            case (int)Directions.Left:
                                lookDir = (int)Directions.Down;
                                break;
                            case (int)Directions.Right:
                                lookDir = (int)Directions.Up;
                                break;
                        }
                        ChangeDir(lookDir);
                        moved = true;
                        break;
                    case MoveRouteEnum.Turn90CounterClockwise:
                        switch (Dir)
                        {
                            case (int)Directions.Up:
                                lookDir = (int)Directions.Left;
                                break;
                            case (int)Directions.Down:
                                lookDir = (int)Directions.Right;
                                break;
                            case (int)Directions.Left:
                                lookDir = (int)Directions.Up;
                                break;
                            case (int)Directions.Right:
                                lookDir = (int)Directions.Down;
                                break;
                        }
                        ChangeDir(lookDir);
                        moved = true;
                        break;
                    case MoveRouteEnum.Turn180:
                        switch (Dir)
                        {
                            case (int)Directions.Up:
                                lookDir = (int)Directions.Down;
                                break;
                            case (int)Directions.Down:
                                lookDir = (int)Directions.Up;
                                break;
                            case (int)Directions.Left:
                                lookDir = (int)Directions.Right;
                                break;
                            case (int)Directions.Right:
                                lookDir = (int)Directions.Left;
                                break;
                        }
                        ChangeDir(lookDir);
                        moved = true;
                        break;
                    case MoveRouteEnum.TurnRandomly:
                        ChangeDir((byte)Globals.Rand.Next(0, 4));
                        moved = true;
                        break;
                    case MoveRouteEnum.Wait100:
                        MoveTimer = Globals.Timing.TimeMs + 100;
                        moved = true;
                        break;
                    case MoveRouteEnum.Wait500:
                        MoveTimer = Globals.Timing.TimeMs + 500;
                        moved = true;
                        break;
                    case MoveRouteEnum.Wait1000:
                        MoveTimer = Globals.Timing.TimeMs + 1000;
                        moved = true;
                        break;
                    default:
                        //Gonna end up returning false because command not found
                        return false;
                }
                if (moved || MoveRoute.IgnoreIfBlocked)
                {
                    MoveRoute.ActionIndex++;
                    if (MoveRoute.ActionIndex >= MoveRoute.Actions.Count)
                    {
                        if (MoveRoute.RepeatRoute) MoveRoute.ActionIndex = 0;
                        MoveRoute.Complete = true;
                    }
                }
                if (moved && MoveTimer < Globals.Timing.TimeMs)
                {
                    MoveTimer = Globals.Timing.TimeMs + (long)GetMovementTime();
                }
            }
            return true;
        }

        public virtual bool IsPassable()
        {
            return Passable;
        }

        //Returns the amount of time required to traverse 1 tile
        public virtual float GetMovementTime()
        {
            var time = 1000f / (float)(1 + Math.Log(Stat[(int)Stats.Speed].Value()));
            if (Blocking)
            {
                time += time * (float)Options.BlockingSlow;
            }
            return Math.Min(1000f, time);
        }

        public virtual EntityTypes GetEntityType()
        {
            return EntityTypes.GlobalEntity;
        }

        public virtual void Move(byte moveDir, Client client, bool dontUpdate = false, bool correction = false)
        {
            var xOffset = 0;
            var yOffset = 0;
            Dir = moveDir;
            if (MoveTimer < Globals.Timing.TimeMs && CastTime <= 0)
            {
                var tile = new TileHelper(MapId, X, Y);
                switch (moveDir)
                {
                    case 0: //Up
                        yOffset--;
                        break;
                    case 1: //Down
                        yOffset++;
                        break;
                    case 2: //Left
                        xOffset--;
                        break;
                    case 3: //Right
                        xOffset++;
                        break;
                    case 4: //NW
                        yOffset--;
                        xOffset--;
                        break;
                    case 5: //NE
                        yOffset--;
                        xOffset++;
                        break;
                    case 6: //SW
                        yOffset++;
                        xOffset--;
                        break;
                    case 7: //SE
                        yOffset++;
                        xOffset++;
                        break;
                }

                if (tile.Translate(xOffset, yOffset))
                {
                    X = tile.GetX();
                    Y = tile.GetY();
                    if (MapId != tile.GetMapId())
                    {
                        var oldMap = MapInstance.Get(MapId);
                        if (oldMap != null) oldMap.RemoveEntity(this);
                        var newMap = MapInstance.Get(tile.GetMapId());
                        if (newMap != null) newMap.AddEntity(this);
                    }
                    MapId = tile.GetMapId();
                    if (dontUpdate == false)
                    {
                        if (GetType() == typeof(EventPageInstance))
                        {
                            if (client != null)
                            {
                                PacketSender.SendEntityMoveTo(client, this, correction);
                            }
                            else
                            {
                                PacketSender.SendEntityMove(this, correction);
                            }
                        }
                        else
                        {
                            PacketSender.SendEntityMove(this, correction);
                        }
                        //Check if moving into a projectile.. if so this npc needs to be hit
                        var myMap = MapInstance.Get(MapId);
                        if (myMap != null)
                        {
                            var localMaps = myMap.GetSurroundingMaps(true);
                            foreach (var map in localMaps)
                            {
                                var projectiles = map.MapProjectiles;
                                foreach (var projectile in projectiles)
                                {
                                    if (projectile.GetType() == typeof(Projectile))
                                    {
                                        var proj = projectile;
                                        foreach (var spawn in proj.Spawns)
                                        {
                                            if (spawn != null && spawn.MapId == MapId && spawn.X == X &&
                                                spawn.Y == Y && spawn.Z == Z)
                                            {
                                                if (spawn.HitEntity(this))
                                                {
                                                    spawn.Parent.KillSpawn(spawn);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        MoveTimer = Globals.Timing.TimeMs + (long)GetMovementTime();
                    }
                    if (TryToChangeDimension() && dontUpdate == true)
                    {
                        PacketSender.UpdateEntityZDimension(this, (byte)Z);
                    }
                    var attribute = MapInstance.Get(MapId).Attributes[X, Y];
                    if (this.GetType() != typeof(EventPageInstance))
                    {
                        //Check for slide tiles
                        if (attribute != null && attribute.Type == MapAttributes.Slide)
                        {
                            if (((MapSlideAttribute)attribute).Direction > 0)
                            {
                                Dir = (byte)(((MapSlideAttribute)attribute).Direction - 1);
                            } //If sets direction, set it.
                            var dash = new DashInstance(this, 1, (byte)Dir);
                        }
                    }
                }
            }
        }

        public void ChangeDir(byte dir)
        {
            Dir = dir;
            if (GetType() == typeof(EventPageInstance))
            {
                if (((EventPageInstance)this).Client != null)
                {
                    PacketSender.SendEntityDirTo(((EventPageInstance)this).Client, this);
                }
                else
                {
                    PacketSender.SendEntityDir(this);
                }
            }
            else
            {
                PacketSender.SendEntityDir(this);
            }
        }

        // Change the dimension if the player is on a gateway
        public bool TryToChangeDimension()
        {
            if (X < Options.MapWidth && X >= 0)
            {
                if (Y < Options.MapHeight && Y >= 0)
                {
                    var attribute = MapInstance.Get(MapId).Attributes[X, Y];
                    if (attribute != null && attribute.Type == MapAttributes.ZDimension)
                    {
                        if (((MapZDimensionAttribute)attribute).GatewayTo > 0)
                        {
                            Z = (byte)(((MapZDimensionAttribute)attribute).GatewayTo - 1);
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        //Misc
        public sbyte GetDirectionTo(EntityInstance target)
        {
            int xDiff = 0, yDiff = 0;
            var myGrid = MapInstance.Get(MapId).MapGrid;
            //Loop through surrouding maps to generate a array of open and blocked points.
            for (var x = MapInstance.Get(MapId).MapGridX - 1; x <= MapInstance.Get(MapId).MapGridX + 1; x++)
            {
                if (x == -1 || x >= DbInterface.MapGrids[myGrid].Width) continue;
                for (var y = MapInstance.Get(MapId).MapGridY - 1; y <= MapInstance.Get(MapId).MapGridY + 1; y++)
                {
                    if (y == -1 || y >= DbInterface.MapGrids[myGrid].Height) continue;
                    if (DbInterface.MapGrids[myGrid].MyGrid[x, y] != Guid.Empty && DbInterface.MapGrids[myGrid].MyGrid[x, y] == target.MapId)
                    {
                        xDiff = (x - MapInstance.Get(MapId).MapGridX) * Options.MapWidth + target.X - X;
                        yDiff = (y - MapInstance.Get(MapId).MapGridY) * Options.MapHeight + target.Y - Y;
                        if (Math.Abs(xDiff) > Math.Abs(yDiff))
                        {
                            if (xDiff < 0) return (int)Directions.Left;
                            if (xDiff > 0) return (int)Directions.Right;
                        }
                        else
                        {
                            if (yDiff < 0) return (int)Directions.Up;
                            if (yDiff > 0) return (int)Directions.Down;
                        }
                    }
                }
            }

            return -1;
        }

        public virtual void SendStatUpdate(int index)
        {
            PacketSender.SendEntityStats(this);
        }

        //Combat
        public virtual int CalculateAttackTime()
        {
            return
                (int)
                (Options.MaxAttackRate +
                 (float)
                 ((Options.MinAttackRate - Options.MaxAttackRate) *
                  (((float)Options.MaxStatValue - Stat[(int)Stats.Speed].Value()) / (float)Options.MaxStatValue)));
        }
        public void TryBlock(bool blocking)
        {
            if (AttackTimer < Globals.Timing.TimeMs)
            {
                if (blocking && !Blocking && AttackTimer < Globals.Timing.TimeMs)
                {
                    Blocking = true;
                    PacketSender.SendEntityAttack(this, -1);
                }
                else if (!blocking && Blocking)
                {
                    Blocking = false;
                    AttackTimer = Globals.Timing.TimeMs + CalculateAttackTime();
                    PacketSender.SendEntityAttack(this, 0);
                }
            }
        }
        public virtual int GetWeaponDamage()
        {
            return 0;
        }
        public virtual bool CanAttack(EntityInstance en, SpellBase spell)
        {
            if (CastTime > 0) return false;
            return true;
        }
        public virtual void ProcessRegen()
        {
        }

        public int GetVital(int vital)
        {
            return _vital[vital];
        }

        public int[] GetVitals()
        {
            int[] vitals = new int[(int)Vitals.VitalCount];
            for (int i = 0; i<(int) Vitals.VitalCount; i++)
            {
                vitals[i] = GetVital(i);
            }
            return vitals;
        }

        public int GetVital(Vitals vital)
        {
            return GetVital((int)vital);
        }
        public void SetVital(int vital, int value)
        {
            if (value < 0) value = 0;
            if (GetMaxVital(vital) < value)
                value = GetMaxVital(vital);
            _vital[vital] = value;
            PacketSender.SendEntityVitals(this);
        }
        public void SetVital(Vitals vital, int value)
        {
            SetVital((int)vital, value);
        }
        public virtual int GetMaxVital(int vital)
        {
            return _maxVital[vital];
        }
        public virtual int GetMaxVital(Vitals vital)
        {
            return GetMaxVital((int)vital);
        }
        public int[] GetMaxVitals()
        {
            int[] vitals = new int[(int)Vitals.VitalCount];
            for (int i = 0; i < (int)Vitals.VitalCount; i++)
            {
                vitals[i] = GetMaxVital(i);
            }
            return vitals;
        }
        public void SetMaxVital(int vital, int value)
        {
            if (value <= 0 && vital == (int)Vitals.Health) value = 1; //Must have at least 1 hp
            if (value < 0 && vital == (int)Vitals.Mana) value = 0; //Can't have less than 0 mana
            _maxVital[vital] = value;
            if (value < GetVital(vital))
                SetVital(vital, value);
            PacketSender.SendEntityVitals(this);
        }
        public void SetMaxVital(Vitals vital, int value)
        {
            SetMaxVital((int)vital, value);
        }
        public bool HasVital(Vitals vital)
        {
            return GetVital(vital) > 0;
        }
        public bool IsFullVital(Vitals vital)
        {
            return GetVital(vital) == GetMaxVital(vital);
        }
        //Vitals
        public void RestoreVital(Vitals vital)
        {
            SetVital(vital, GetMaxVital(vital));
        }
        public void AddVital(Vitals vital, int amount)
        {
            if (vital >= Vitals.VitalCount) return;

            var vitalId = (int)vital;
            var maxVitalValue = GetMaxVital(vitalId);
            var safeAmount = Math.Min(amount, int.MaxValue - maxVitalValue);
            SetVital(vital, GetVital(vital) + safeAmount);
        }
        public void SubVital(Vitals vital, int amount)
        {
            if (vital >= Vitals.VitalCount) return;

            //Check for any shields.
            var statuses = Statuses.Values.ToArray();
            foreach (var status in statuses)
            {
                if (status.Type == StatusTypes.Shield)
                {
                    status.DamageShield(vital, ref amount);
                }
            }

            var vitalId = (int)vital;
            var maxVitalValue = GetMaxVital(vitalId);
            var safeAmount = Math.Min(amount, GetVital(vital));
            SetVital(vital, GetVital(vital) - safeAmount);
        }
        //Stats
        public virtual int GetStatBuffs(Stats statType)
        {
            return 0;
        }

        public virtual int[] GetStatValues()
        {
            var stats = new int[(int) Stats.StatCount];
            for (int i = 0; i < (int) Stats.StatCount; i++)
                stats[i] = Stat[i].Value();
            return stats;
        }

        //Attacking with projectile
        public virtual void TryAttack(EntityInstance enemy, ProjectileBase projectile, SpellBase parentSpell, ItemBase parentItem, byte projectileDir)
        {
            if (enemy.GetType() == typeof(Resource) && parentSpell != null) return;

            //Check for taunt status and trying to attack a target that has not taunted you.
            var statuses = Statuses.Values.ToArray();
            foreach (var status in statuses)
            {
                if (status.Type == StatusTypes.Taunt)
                {
                    if (Target != enemy)
                    {
                        PacketSender.SendActionMsg(this, Strings.Combat.miss, CustomColors.Missed);
                        return;
                    }
                }
            }

            //Check if the target is blocking facing in the direction against you
            if (enemy.Blocking)
            {
                var d = Dir;

                if (projectile != null)
                {
                    d = projectileDir;
                }

                if (enemy.Dir == (int)Directions.Left && d == (int)Directions.Right)
                {
                    PacketSender.SendActionMsg(enemy, Strings.Combat.blocked, CustomColors.Blocked);
                    return;
                }
                else if (enemy.Dir == (int)Directions.Right && d == (int)Directions.Left)
                {
                    PacketSender.SendActionMsg(enemy, Strings.Combat.blocked, CustomColors.Blocked);
                    return;
                }
                else if (enemy.Dir == (int)Directions.Up && d == (int)Directions.Down)
                {
                    PacketSender.SendActionMsg(enemy, Strings.Combat.blocked, CustomColors.Blocked);
                    return;
                }
                else if (enemy.Dir == (int)Directions.Down && d == (int)Directions.Up)
                {
                    PacketSender.SendActionMsg(enemy, Strings.Combat.blocked, CustomColors.Blocked);
                    return;
                }
            }

            if (parentSpell != null)
            {
                TryAttack(enemy, parentSpell);
            }

            if (GetType() == typeof(Player) && enemy.GetType() == typeof(Player))
            {
                //Player interaction common events
                foreach (EventBase evt in EventBase.Lookup.Values)
                {
                    if (evt != null)
                    {
                        ((Player)enemy).StartCommonEvent(evt, CommonEventTrigger.PlayerInteract);
                    }
                }

                if (MapInstance.Get(MapId).ZoneType == MapZones.Safe)
                {
                    return;
                }
                if (MapInstance.Get(enemy.MapId).ZoneType == MapZones.Safe)
                {
                    return;
                }
                if (((Player)this).InParty((Player)enemy) == true) return;
            }

            if (parentSpell == null)
            {
                Attack(enemy, parentItem.Damage, 0, (DamageType)parentItem.DamageType, (Stats)parentItem.ScalingStat,
                    parentItem.Scaling, parentItem.CritChance, parentItem.CritMultiplier, null, null, true);
            }

            //If projectile, check if a splash spell is applied
            if (projectile != null)
            {
                if (projectile.SpellId != Guid.Empty)
                {
                    var s = projectile.Spell;
                    if (s != null)
                        HandleAoESpell(projectile.SpellId, s.Combat.HitRadius, enemy.MapId, enemy.X, enemy.Y,
                            null);

                    //Check that the npc has not been destroyed by the splash spell
                    //TODO: Actually implement this, since null check is wrong.
                    if (enemy == null)
                    {
                        return;
                    }
                }
                if (enemy.GetType() == typeof(Player) || enemy.GetType() == typeof(Npc))
                {
                    if (projectile.Knockback > 0 && projectileDir < 4)
                    //If there is a knockback, knock them backwards and make sure its linear (diagonal player movement not coded).
                    {
                        var dash = new DashInstance(enemy, projectile.Knockback, projectileDir, false, false, false,
                            false);
                    }
                }
            }
        }

        //Attacking with spell
        public virtual void TryAttack(EntityInstance enemy, SpellBase spellBase, bool onHitTrigger = false)
        {
            if (enemy?.GetType() == typeof(Resource)) return;
            if (spellBase == null) return;

            //Check for taunt status and trying to attack a target that has not taunted you.
            var statuses = Statuses.Values.ToArray();
            foreach (var status in statuses)
            {
                if (status.Type == StatusTypes.Taunt)
                {
                    if (Target != enemy)
                    {
                        PacketSender.SendActionMsg(this, Strings.Combat.miss, CustomColors.Missed);
                        return;
                    }
                }
            }

            var deadAnimations = new List<KeyValuePair<Guid, sbyte>>();
            var aliveAnimations = new List<KeyValuePair<Guid, sbyte>>();

            //Only count safe zones and friendly fire if its a dangerous spell! (If one has been used)
            if (!spellBase.Combat.Friendly && (spellBase.Combat.TargetType != (int)SpellTargetTypes.Self || onHitTrigger))
            {
                //If about to hit self with an unfriendly spell (maybe aoe?) return
                if (enemy == this && spellBase.Combat.Effect != StatusTypes.OnHit) return;

                //Check for parties and safe zones, friendly fire off (unless its healing)
                if (enemy.GetType() == typeof(Player) && GetType() == typeof(Player))
                {
                    if (((Player)this).InParty((Player)enemy) == true) return;
                }

                if (enemy.GetType() == typeof(Npc) && GetType() == typeof(Npc))
                {
                    if (!((Npc)this).CanNpcCombat(enemy, spellBase.Combat.Friendly))
                    {
                        return;
                    }
                }

                //Check if either the attacker or the defender is in a "safe zone" (Only apply if combat is PVP)
                if (enemy.GetType() == typeof(Player) && GetType() == typeof(Player))
                {
                    if (MapInstance.Get(MapId).ZoneType == MapZones.Safe)
                    {
                        return;
                    }
                    if (MapInstance.Get(enemy.MapId).ZoneType == MapZones.Safe)
                    {
                        return;
                    }
                }

                if (!CanAttack(enemy, spellBase)) return;
            }
            else
            {
                //Friendly Spell! Do not attack other players/npcs around us.
                if (enemy.GetType() == typeof(Player) && GetType() == typeof(Player))
                {
                    if (!((Player)this).InParty((Player)enemy) && this != enemy) return;
                }
                if (enemy.GetType() == typeof(Npc) && GetType() == typeof(Npc))
                {
                    if (!((Npc)this).CanNpcCombat(enemy, spellBase.Combat.Friendly))
                    {
                        return;
                    }
                }
                if (enemy.GetType() != GetType()) return; //Don't let players aoe heal npcs. Don't let npcs aoe heal players.
            }

            if (spellBase.HitAnimationId != Guid.Empty && (spellBase.Combat.Effect != StatusTypes.OnHit || onHitTrigger))
            {
                deadAnimations.Add(new KeyValuePair<Guid, sbyte>(spellBase.HitAnimationId, (sbyte)Directions.Up));
                aliveAnimations.Add(new KeyValuePair<Guid, sbyte>(spellBase.HitAnimationId, (sbyte)Directions.Up));
            }

            var statBuffTime = -1;
            for (var i = 0; i < (int)Stats.StatCount; i++)
            {
                enemy.Stat[i].AddBuff(new EntityBuff(spellBase, spellBase.Combat.StatDiff[i] + 
                    ((enemy.Stat[i].Stat * spellBase.Combat.PercentageStatDiff[i]) / 100), spellBase.Combat.Duration));
                if (spellBase.Combat.StatDiff[i] != 0 || spellBase.Combat.PercentageStatDiff[i] != 0)
                    statBuffTime = spellBase.Combat.Duration;
            }

            if (statBuffTime == -1)
            {
                if (spellBase.Combat.HoTDoT && spellBase.Combat.HotDotInterval > 0)
                {
                    statBuffTime = spellBase.Combat.Duration;
                }
            }

            if (spellBase.Combat.Effect > 0) //Handle status effects
            {
                //Check for onhit effect to avoid the onhit effect recycling.
                if (!(onHitTrigger && spellBase.Combat.Effect == StatusTypes.OnHit))
                {
                    new StatusInstance(enemy, spellBase, spellBase.Combat.Effect, spellBase.Combat.Duration, spellBase.Combat.TransformSprite);
                    PacketSender.SendActionMsg(enemy, Strings.Combat.status[(int)spellBase.Combat.Effect], CustomColors.Status);

                    //Set the enemies target if a taunt spell
                    if (spellBase.Combat.Effect == StatusTypes.Taunt)
                    {
                        enemy.Target = this;
                        if (enemy.GetType() == typeof(Player))
                        {
                            PacketSender.SetPlayerTarget(((Player)enemy).Client, Id);
                        }
                    }

                    //If an onhit or shield status bail out as we don't want to do any damage.
                    if (spellBase.Combat.Effect == StatusTypes.OnHit || spellBase.Combat.Effect == StatusTypes.Shield) return;
                }
            }
            else
            {
                if (statBuffTime > -1) new StatusInstance(enemy, spellBase, spellBase.Combat.Effect, statBuffTime, "");
            }

            var damageHealth = spellBase.Combat.VitalDiff[0];
            var damageMana = spellBase.Combat.VitalDiff[1];

            Attack(enemy, damageHealth, damageMana, (DamageType)spellBase.Combat.DamageType, (Stats)spellBase.Combat.ScalingStat, spellBase.Combat.Scaling, spellBase.Combat.CritChance, spellBase.Combat.CritMultiplier, deadAnimations, aliveAnimations);

            //Handle DoT/HoT spells]
            if (spellBase.Combat.HoTDoT)
            {
                var doTFound = false;
                for (var i = 0; i < enemy.DoT.Count; i++)
                {
                    if (enemy.DoT[i].SpellBase.Id == spellBase.Id ||
                        enemy.DoT[i].Target == this)
                    {
                        doTFound = true;
                    }
                }
                if (doTFound == false) //no duplicate DoT/HoT spells.
                {
                    new DoTInstance(this, spellBase.Id, enemy);
                }
            }
        }

        //Attacking with weapon or unarmed.
        public virtual void TryAttack(EntityInstance enemy)
        {
            //See player and npc override of this virtual void
        }

        //Attack using a weapon or unarmed
        public virtual void TryAttack(EntityInstance enemy, int baseDamage, DamageType damageType, Stats scalingStat, int scaling, int critChance, double critMultiplier, List<KeyValuePair<Guid, sbyte>> deadAnimations = null, List<KeyValuePair<Guid, sbyte>> aliveAnimations = null, ItemBase weapon = null)
        {
            if ((AttackTimer > Globals.Timing.TimeMs || Blocking)) return;

            //Check for parties and safe zones, friendly fire off (unless its healing)
            if (enemy.GetType() == typeof(Player) && GetType() == typeof(Player))
            {
                if (((Player)this).InParty((Player)enemy) == true) return;
            }

            //Check if either the attacker or the defender is in a "safe zone" (Only apply if combat is PVP)
            if (enemy.GetType() == typeof(Player) && GetType() == typeof(Player))
            {
                //Player interaction common events
                foreach (EventBase evt in EventBase.Lookup.Values)
                {
                    if (evt != null)
                    {
                        ((Player)enemy).StartCommonEvent(evt, CommonEventTrigger.PlayerInteract);
                    }
                }

                if (MapInstance.Get(MapId).ZoneType == MapZones.Safe)
                {
                    return;
                }
                if (MapInstance.Get(enemy.MapId).ZoneType == MapZones.Safe)
                {
                    return;
                }
            }

            //Check for taunt status and trying to attack a target that has not taunted you.
            var statusList = Statuses.Values.ToArray();
            foreach (var status in statusList)
            {
                if (status.Type == StatusTypes.Taunt)
                {
                    if (Target != enemy)
                    {
                        PacketSender.SendActionMsg(this, Strings.Combat.miss, CustomColors.Missed);
                        return;
                    }
                }
            }

            AttackTimer = Globals.Timing.TimeMs + CalculateAttackTime();
            //Check if the attacker is blinded.
            if (IsOneBlockAway(enemy))
            {
                var statuses = Statuses.Values.ToArray();
                foreach (var status in statuses)
                {
                    if (status.Type == StatusTypes.Stun || status.Type == StatusTypes.Blind || status.Type == StatusTypes.Sleep)
                    {
                        PacketSender.SendActionMsg(this, Strings.Combat.miss, CustomColors.Missed);
                        PacketSender.SendEntityAttack(this, CalculateAttackTime());
                        return;
                    }
                }
            }

            Attack(enemy, baseDamage, 0, damageType, scalingStat, scaling, critChance, critMultiplier, deadAnimations,
                aliveAnimations, true);

            //If we took damage lets reset our combat timer
            enemy.CombatTimer = Globals.Timing.TimeMs + 5000;
        }

        public void Attack(EntityInstance enemy, int baseDamage, int secondaryDamage, DamageType damageType, Stats scalingStat,
            int scaling, int critChance, double critMultiplier, List<KeyValuePair<Guid, sbyte>> deadAnimations = null,
            List<KeyValuePair<Guid, sbyte>> aliveAnimations = null, bool isAutoAttack = false)
        {
	        bool damagingAttack = (baseDamage > 0);
            if (enemy == null) return;

            //Remove stealth
            foreach (var status in this.Statuses.Values.ToArray())
            {
                if (status.Type == StatusTypes.Stealth)
                {
                    status.RemoveStatus();
                }
            }

            //Check for enemy statuses
            var statuses = enemy.Statuses.Values.ToArray();
			foreach (var status in statuses)
			{
                //Invulnerability ignore
                if (status.Type == StatusTypes.Invulnerable)
				{
					PacketSender.SendActionMsg(enemy, Strings.Combat.invulnerable, CustomColors.Invulnerable);

					// Add a timer before able to make the next move.
					if (GetType() == typeof(Npc))
					{
						((Npc)this).MoveTimer = Globals.Timing.TimeMs + (long)GetMovementTime();
					}

					return;
				}
            }

			//Is this a critical hit?
			if (Globals.Rand.Next(1, 101) > critChance)
            {
                critMultiplier = 1;
            }
            else
            {
                PacketSender.SendActionMsg(enemy, Strings.Combat.critical, CustomColors.Critical);
            }

            //Calculate Damages
            if (baseDamage != 0)
            {
                baseDamage = Formulas.CalculateDamage(baseDamage, damageType, scalingStat, scaling, critMultiplier, this, enemy);

	            if (baseDamage < 0 && damagingAttack) { baseDamage = 0; }

                if (baseDamage > 0 && enemy.HasVital(Vitals.Health))
                {
                    enemy.SubVital(Vitals.Health, (int)baseDamage);
                    switch (damageType)
                    {
                        case DamageType.Physical:
                            PacketSender.SendActionMsg(enemy, Strings.Combat.removesymbol + (int)baseDamage,
                                CustomColors.PhysicalDamage);
                            break;
                        case DamageType.Magic:
                            PacketSender.SendActionMsg(enemy, Strings.Combat.removesymbol + (int)baseDamage,
                                CustomColors.MagicDamage);
                            break;
                        case DamageType.True:
                            PacketSender.SendActionMsg(enemy, Strings.Combat.removesymbol + (int)baseDamage,
                                CustomColors.TrueDamage);
                            break;
                    }
                    enemy.CombatTimer = Globals.Timing.TimeMs + 5000;

                    foreach (var status in statuses)
                    {
                        //Wake up any sleeping targets
                        if (status.Type == StatusTypes.Sleep)
                        {
                            status.RemoveStatus();
                        }
                    }

                    //No Matter what, if we attack the entitiy, make them chase us
                    if (enemy.GetType() == typeof(Npc))
                    {
                        var dmgMap = ((Npc)enemy).DamageMap;
                        if (dmgMap.ContainsKey(this))
                        {
                            dmgMap[this] += baseDamage;
                        }
                        else
                        {
                            dmgMap[this] = baseDamage;
                        }
                        long dmg = baseDamage;
                        var target = this;
                        foreach (var pair in dmgMap)
                        {
                            if (pair.Value > dmg)
                            {
                                target = pair.Key;
                                dmg = pair.Value;
                            }
                        }
                        if (((Npc)enemy).Base.FocusHighestDamageDealer)
                        {
                            ((Npc)enemy).AssignTarget(target);
                        }
                        else
                        {
                            ((Npc)enemy).AssignTarget(this);
                        }
                    }
                    enemy.NotifySwarm(this);
                }
                else if (baseDamage < 0 && !enemy.IsFullVital(Vitals.Health))
                {
                    enemy.SubVital(Vitals.Health, (int)baseDamage);
                    PacketSender.SendActionMsg(enemy, Strings.Combat.addsymbol + (int)Math.Abs(baseDamage), CustomColors.Heal);
                }
            }
            if (secondaryDamage != 0)
            {
                secondaryDamage = Formulas.CalculateDamage(secondaryDamage, damageType, scalingStat, scaling, critMultiplier, this, enemy);

	            if (secondaryDamage < 0 && damagingAttack) { secondaryDamage = 0; }

				if (secondaryDamage > 0 && enemy.HasVital(Vitals.Mana))
                {
                    //If we took damage lets reset our combat timer
                    enemy.SubVital(Vitals.Mana, (int)secondaryDamage);
                    enemy.CombatTimer = Globals.Timing.TimeMs + 5000;
                    PacketSender.SendActionMsg(enemy, Strings.Combat.removesymbol + (int)secondaryDamage,
                        CustomColors.RemoveMana);

                    enemy.CombatTimer = Globals.Timing.TimeMs + 5000;

                    //No Matter what, if we attack the entitiy, make them chase us
                    if (enemy.GetType() == typeof(Npc))
                    {
                        var dmgMap = ((Npc)enemy).DamageMap;
                        var target = this;
                        long dmg = 0;
                        foreach (var pair in dmgMap)
                        {
                            if (pair.Value > dmg)
                            {
                                target = pair.Key;
                                dmg = pair.Value;
                            }
                        }
                        if (((Npc)enemy).Base.FocusHighestDamageDealer)
                        {
                            ((Npc)enemy).AssignTarget(target);
                        }
                        else
                        {
                            ((Npc)enemy).AssignTarget(this);
                        }
                    }
                    enemy.NotifySwarm(this);
                }
                else if (secondaryDamage < 0 && !enemy.IsFullVital(Vitals.Mana))
                {
                    enemy.SubVital(Vitals.Mana, (int)secondaryDamage);
                    PacketSender.SendActionMsg(enemy, Strings.Combat.addsymbol + (int)Math.Abs(secondaryDamage), CustomColors.AddMana);
                }
            }

            //Check for lifesteal
            if (GetType() == typeof(Player) && enemy.GetType() != typeof(Resource))
            {
                decimal lifesteal = ((Player)this).GetLifeSteal() / 100;
                decimal healthRecovered = lifesteal * baseDamage;
                if (healthRecovered > 0) //Don't send any +0 msg's.
                {
                    AddVital(Vitals.Health, (int)healthRecovered);
                    PacketSender.SendActionMsg(this, Strings.Combat.addsymbol + (int)healthRecovered, CustomColors.Heal);
                    PacketSender.SendEntityVitals(this);
                }
            }
            //Dead entity check
            if (enemy.GetVital(Vitals.Health) <= 0)
            {
                KilledEntity(enemy);
                if (enemy.GetType() == typeof(Npc) || enemy.GetType() == typeof(Resource))
                {
                    enemy.Die(100, this);
                }
                else
                {
                    enemy.Die(Options.ItemDropChance);

                    //PVP Kill common events
                    if (this.GetType() == typeof(Player))
                    {
                        if (MapInstance.Get(MapId).ZoneType != MapZones.Arena)
                        {
                            foreach (EventBase evt in EventBase.Lookup.Values)
                            {
                                if (evt != null)
                                {
                                    ((Player)this).StartCommonEvent(evt, CommonEventTrigger.PVPKill);
                                    ((Player)enemy).StartCommonEvent(evt, CommonEventTrigger.PVPDeath);
                                }
                            }
                        }
                    }
                }
                if (deadAnimations != null)
                {
                    foreach (var anim in deadAnimations)
                    {
                        PacketSender.SendAnimationToProximity(anim.Key, -1, Guid.Empty, enemy.MapId, (byte)enemy.X, (byte)enemy.Y, anim.Value);
                    }
                }
            }
            else
            {
                //Hit him, make him mad and send the vital update.
                PacketSender.SendEntityVitals(enemy);
                PacketSender.SendEntityStats(enemy);
                if (aliveAnimations != null)
                {
                    foreach (var anim in aliveAnimations)
                    {
                        PacketSender.SendAnimationToProximity(anim.Key, 1, enemy.Id, enemy.MapId, 0,0, anim.Value);
                    }
                }

                //Check for any onhit damage bonus effects!
                CheckForOnhitAttack(enemy, isAutoAttack);
            }
            // Add a timer before able to make the next move.
            if (GetType() == typeof(Npc))
            {
                ((Npc)this).MoveTimer = Globals.Timing.TimeMs + (long)GetMovementTime();
            }
        }

        void CheckForOnhitAttack(EntityInstance enemy, bool isAutoAttack)
        {
            if (isAutoAttack) //Ignore spell damage.
            {
                foreach (var status in this.Statuses.Values.ToArray())
                {
                    if (status.Type == StatusTypes.OnHit)
                    {
                        TryAttack(enemy, status.Spell, true);
                        status.RemoveStatus();
                    }
                }
            }
        }

        public virtual void KilledEntity(EntityInstance en)
        {
        }

        public virtual void CastSpell(Guid spellId, int spellSlot = -1)
        {
            var spellBase = SpellBase.Get(spellId);
            if (spellBase != null)
            {
                switch (spellBase.SpellType)
                {
                    case SpellTypes.CombatSpell:
                    case SpellTypes.Event:

                        switch (spellBase.Combat.TargetType)
                        {
                            case SpellTargetTypes.Self:
                                if (spellBase.HitAnimationId != Guid.Empty && spellBase.Combat.Effect != StatusTypes.OnHit)
                                {
                                    PacketSender.SendAnimationToProximity(spellBase.HitAnimationId, 1, Id, MapId, 0, 0, (sbyte)Dir); //Target Type 1 will be global entity
                                }
                                TryAttack(this, spellBase);
                                break;
                            case SpellTargetTypes.Single:
                                if (CastTarget == null) return;

                                //If target has stealthed we cannot hit the spell.
                                foreach (var status in CastTarget.Statuses.Values.ToArray())
                                {
                                    if (status.Type == StatusTypes.Stealth)
                                    {
                                        return;
                                    }
                                }

                                if (spellBase.Combat.HitRadius > 0) //Single target spells with AoE hit radius'
                                {
                                    HandleAoESpell(spellId, spellBase.Combat.HitRadius, CastTarget.MapId, CastTarget.X, CastTarget.Y, null);
                                }
                                else
                                {
                                    TryAttack(CastTarget, spellBase);
                                }
                                break;
                            case SpellTargetTypes.AoE:
                                HandleAoESpell(spellId, spellBase.Combat.HitRadius, MapId, X, Y, null);
                                break;
                            case SpellTargetTypes.Projectile:
                                var projectileBase = spellBase.Combat.Projectile;
                                if (projectileBase != null)
                                {
                                    MapInstance.Get(MapId).SpawnMapProjectile(this, projectileBase, spellBase, null, MapId, (byte)X, (byte)Y, (byte)Z, (byte)Dir, CastTarget);
                                }
                                break;
                            case SpellTargetTypes.OnHit:
                                if (spellBase.Combat.Effect == StatusTypes.OnHit)
                                {
                                    new StatusInstance(this, spellBase, StatusTypes.OnHit, spellBase.Combat.OnHitDuration, spellBase.Combat.TransformSprite);
                                    PacketSender.SendActionMsg(this, Strings.Combat.status[(int) spellBase.Combat.Effect], CustomColors.Status);
                                }
                                break;
                            default:
                                break;
                        }
                        break;
                    case SpellTypes.Warp:
                        if (GetType() == typeof(Player))
                        {
                            Warp(spellBase.Warp.MapId, (byte)spellBase.Warp.X, (byte)spellBase.Warp.Y, (spellBase.Warp.Dir - 1) == -1 ? (byte)this.Dir : (byte)(spellBase.Warp.Dir - 1));
                        }
                        break;
                    case SpellTypes.WarpTo:
                        if (CastTarget == null) return;
                        HandleAoESpell(spellId, spellBase.Combat.CastRange, MapId, X, Y, CastTarget);
                        break;
                    case SpellTypes.Dash:
                        PacketSender.SendActionMsg(this, Strings.Combat.dash, CustomColors.Dash);
                        var dash = new DashInstance(this, spellBase.Combat.CastRange, (byte)Dir, Convert.ToBoolean(spellBase.Dash.IgnoreMapBlocks),
                            Convert.ToBoolean(spellBase.Dash.IgnoreActiveResources), Convert.ToBoolean(spellBase.Dash.IgnoreInactiveResources), Convert.ToBoolean(spellBase.Dash.IgnoreZDimensionAttributes));
                        break;
                    default:
                        break;
                }
                if (spellSlot >= 0 && spellSlot < Options.MaxPlayerSkills)
                {
                    decimal cooldownReduction = 1;

                    if (GetType() == typeof(Player)) //Only apply cdr for players with equipment
                    {
                        cooldownReduction = (1 - ((decimal)((Player)this).GetCooldownReduction() / 100));
                    }

                    Spells[spellSlot].SpellCd = Globals.Timing.RealTimeMs + (int)(spellBase.CooldownDuration * cooldownReduction);
                    if (GetType() == typeof(Player))
                    {
                        PacketSender.SendSpellCooldown(((Player)this).Client, spellSlot);
                    }
                }
            }
        }

        private void HandleAoESpell(Guid spellId, int range, Guid startMapId, int startX, int startY, EntityInstance spellTarget)
        {
            var spellBase = SpellBase.Get(spellId);
            var targetsHit = new List<EntityInstance>();
            if (spellBase != null)
            {
                for (var x = startX - range; x <= startX + range; x++)
                {
                    for (var y = startY - range; y <= startY + range; y++)
                    {
                        var tempMap = MapInstance.Get(startMapId);

                        if (tempMap == null) continue;

                        var x2 = x;
                        var y2 = y;

                        if (y < 0 && tempMap.Up != Guid.Empty)
                        {
                            tempMap = MapInstance.Get(tempMap.Up);
                            y2 = Options.MapHeight + y;
                        }
                        else if (y > Options.MapHeight - 1 && tempMap.Down != Guid.Empty)
                        {
                            tempMap = MapInstance.Get(tempMap.Down);
                            y2 = y - Options.MapHeight;
                        }

                        if (x < 0 && tempMap.Left != Guid.Empty)
                        {
                            tempMap = MapInstance.Get(tempMap.Left);
                            x2 = Options.MapWidth + x;
                        }
                        else if (x > Options.MapWidth - 1 && tempMap.Right != Guid.Empty)
                        {
                            tempMap = MapInstance.Get(tempMap.Right);
                            x2 = x - Options.MapWidth;
                        }

                        if (tempMap == null) continue;

                        var mapEntities = tempMap.GetEntities();
                        for (var i = 0; i < mapEntities.Count; i++)
                        {
                            var t = mapEntities[i];
                            if (t == null || targetsHit.Contains(t)) continue;
                            if (t.GetType() == typeof(Player) || t.GetType() == typeof(Npc))
                            {
                                if (t.MapId == tempMap.Id && t.X == x2 && t.Y == y2)
                                {
                                    if (spellTarget == null || spellTarget == t)
                                    {
                                        targetsHit.Add(t);

                                        //Check to handle a warp to spell
                                        if (spellBase.SpellType == SpellTypes.WarpTo)
                                        {
                                            if (spellTarget != null)
                                            {
                                                Warp(spellTarget.MapId, (byte)spellTarget.X, (byte)spellTarget.Y, (byte)Dir); //Spelltarget used to be Target. I don't know if this is correct or not.
                                            }
                                        }

                                        TryAttack(t, spellBase); //Handle damage
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        //Check if the target is either up, down, left or right of the target on the correct Z dimension.
        protected bool IsOneBlockAway(EntityInstance target)
        {
            var myTile = new TileHelper(MapId, X, Y);
            var enemyTile = new TileHelper(target.MapId, target.X, target.Y);
            if (Z == target.Z)
            {
                myTile.Translate(0, -1);
                if (myTile.Matches(enemyTile)) return true;
                myTile.Translate(0, 2);
                if (myTile.Matches(enemyTile)) return true;
                myTile.Translate(-1, -1);
                if (myTile.Matches(enemyTile)) return true;
                myTile.Translate(2, 0);
                if (myTile.Matches(enemyTile)) return true;
            }
            return false;
        }

        //These functions only work when one block away.
        protected bool IsFacingTarget(EntityInstance target)
        {
            if (IsOneBlockAway(target))
            {
                var myTile = new TileHelper(MapId, X, Y);
                var enemyTile = new TileHelper(target.MapId, target.X, target.Y);
                myTile.Translate(0, -1);
                if (myTile.Matches(enemyTile) && Dir == (int)Directions.Up) return true;
                myTile.Translate(0, 2);
                if (myTile.Matches(enemyTile) && Dir == (int)Directions.Down) return true;
                myTile.Translate(-1, -1);
                if (myTile.Matches(enemyTile) && Dir == (int)Directions.Left) return true;
                myTile.Translate(2, 0);
                if (myTile.Matches(enemyTile) && Dir == (int)Directions.Right) return true;
            }
            return false;
        }

        protected int GetDistanceTo(EntityInstance target)
        {
            if (target != null)
            {
                var myMap = MapInstance.Get(MapId);
                var targetMap = MapInstance.Get(target.MapId);
                if (myMap != null && targetMap != null)
                {
                    //Calculate World Tile of Me
                    var x1 = X + (myMap.MapGridX * Options.MapWidth);
                    var y1 = Y + (myMap.MapGridY * Options.MapHeight);
                    //Calculate world tile of target
                    var x2 = target.X + (targetMap.MapGridX * Options.MapWidth);
                    var y2 = target.Y + (targetMap.MapGridY * Options.MapHeight);
                    return (int)Math.Sqrt(Math.Pow(x1 - x2, 2) + (Math.Pow(y1 - y2, 2)));
                }
            }
            //Something is null.. return a value that is out of range :) 
            return 9999;
        }

        protected bool InRangeOf(EntityInstance target, int range)
        {
            var dist = GetDistanceTo(target);
            if (dist <= range) return true;
            return false;
        }

        public virtual void NotifySwarm(EntityInstance attacker)
        {

        }

        protected byte DirToEnemy(EntityInstance target)
        {
            //Calculate World Tile of Me
            var x1 = X + (MapInstance.Get(MapId).MapGridX * Options.MapWidth);
            var y1 = Y + (MapInstance.Get(MapId).MapGridY * Options.MapHeight);
            //Calculate world tile of target
            var x2 = target.X + (MapInstance.Get(target.MapId).MapGridX * Options.MapWidth);
            var y2 = target.Y + (MapInstance.Get(target.MapId).MapGridY * Options.MapHeight);
            if (Math.Abs(x1 - x2) > Math.Abs(y1 - y2))
            {
                //Left or Right
                if (x1 - x2 < 0)
                {
                    return (byte)Directions.Right;
                }
                else
                {
                    return (byte)Directions.Left;
                }
            }
            else
            {
                //Left or Right
                if (y1 - y2 < 0)
                {
                    return (byte)Directions.Down;
                }
                else
                {
                    return (byte)Directions.Up;
                }
            }
        }

        //Check if the target is either up, down, left or right of the target on the correct Z dimension.
        protected bool IsOneBlockAway(Guid mapId, int x, int y, int z = 0)
        {
            //Calculate World Tile of Me
            var x1 = X + (MapInstance.Get(MapId).MapGridX * Options.MapWidth);
            var y1 = Y + (MapInstance.Get(MapId).MapGridY * Options.MapHeight);
            //Calculate world tile of target
            var x2 = x + (MapInstance.Get(mapId).MapGridX * Options.MapWidth);
            var y2 = y + (MapInstance.Get(mapId).MapGridY * Options.MapHeight);
            if (z == Z)
            {
                if (y1 == y2)
                {
                    if (x1 == x2 - 1)
                    {
                        return true;
                    }
                    else if (x1 == x2 + 1)
                    {
                        return true;
                    }
                }
                if (x1 == x2)
                {
                    if (y1 == y2 - 1)
                    {
                        return true;
                    }
                    else if (y1 == y2 + 1)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        //Spawning/Dying
        public virtual void Die(int dropitems = 0, EntityInstance killer = null)
        {
            if (Items == null) return;

            if (dropitems > 0)
            {
                // Drop items
                for (var n = 0; n < Items.Count; n++)
                {
                    var item = Items[n];
                    if (item == null) continue;

                    var itemBase = ItemBase.Get(item.ItemId);
                    if (itemBase == null) continue;

                    //Don't lose bound items on death for players.
                    if (this.GetType() == typeof(Player))
                    {
                        if (itemBase.Bound)
                        {
                            continue;
                        }
                    }

                    if (Globals.Rand.Next(1, 101) >= dropitems) continue;

                    var map = MapInstance.Get(MapId);
                    map?.SpawnItem(X, Y, item, item.Quantity);

                    var player = this as Player;
                    player?.TakeItemsBySlot(n, item.Quantity);
                }
            }

            var currentMap = MapInstance.Get(MapId);
            if (currentMap != null)
            {
                currentMap.ClearEntityTargetsOf(this);
                currentMap.GetSurroundingMaps()?.ForEach(map => map?.ClearEntityTargetsOf(this));
            }

            DoT?.Clear();
            Statuses?.Clear();
            Stat?.ToList().ForEach(stat => stat?.Reset());

            PacketSender.SendEntityVitals(this);
            Dead = true;
        }

        public virtual bool IsDead()
        {
            return Dead;
        }

        public void Reset()
        {
            for (var i = 0; i < (int)Vitals.VitalCount; i++)
            {
                RestoreVital((Vitals)i);
            }
            Dead = false;
        }

        //Empty virtual functions for players
        public virtual void Warp(Guid newMapId, byte newX, byte newY, bool adminWarp = false)
        {
            Warp(newMapId, newX, newY, (byte)Dir, adminWarp);
        }

        public virtual void Warp(Guid newMapId, byte newX, byte newY, byte newDir, bool adminWarp = false, byte zOverride = 0, bool mapSave = false)
        {
        }

        public virtual EntityPacket EntityPacket(EntityPacket packet = null, Client forClient = null)
        {
            if (packet == null) packet = new EntityPacket();

            packet.EntityId = Id;
            packet.MapId = MapId;
            packet.Name = Name;
            packet.Sprite = Sprite;
            packet.Face = Face;
            packet.Level = Level;
            packet.X = (byte)X;
            packet.Y = (byte)Y;
            packet.Z = (byte)Z;
            packet.Dir = (byte)Dir;
            packet.Passable = Passable;
            packet.HideName = HideName;
            packet.HideEntity = HideEntity;
            packet.Animations = Animations.ToArray();
            packet.Vital = GetVitals();
            packet.MaxVital = GetMaxVitals();
            packet.Stats = GetStatValues();
            packet.StatusEffects = StatusPackets();
            packet.NameColor = NameColor;

            return packet;
        }

        public StatusPacket[] StatusPackets()
        {
            var statuses = Statuses.Values.ToArray();
            var statusPackets = new StatusPacket[statuses.Length];
            for (int i = 0; i < statuses.Length; i++)
            {
                var status = statuses[i];
                int[] vitalShields = null;
                if (status.Type == StatusTypes.Shield)
                {
                    vitalShields = new int[(int)Vitals.VitalCount];
                    for (var x = 0; x < (int)Vitals.VitalCount; x++)
                    {
                        vitalShields[x] = status.shield[x];
                    }
                }

                statusPackets[i] = new StatusPacket(status.Spell.Id, status.Type, status.Data, (int)(status.Duration - Globals.Timing.TimeMs), (int)(status.Duration - status.StartTime), vitalShields);
            }

            return statusPackets;
        }
    }

    public class EntityStat
    {
        private EntityInstance mOwner;
        private Stats mStatType;
        private Dictionary<SpellBase, EntityBuff> mBuff = new Dictionary<SpellBase, EntityBuff>();
        private bool mChanged;

        public int Stat
        {
            get => mOwner.BaseStats[(int)mStatType];
            set => mOwner.BaseStats[(int)mStatType] = value;
        }

        public EntityStat(Stats statType, EntityInstance owner)
        {
            mOwner = owner;
            mStatType = statType;
        }

        public int Value()
        {
            var s = Stat;

            s += mOwner.StatPointAllocations[(int) mStatType];
            s += mOwner.GetStatBuffs(mStatType);

			//Add buffs
			var buffs = mBuff.Values.ToArray();
			foreach (var buff in buffs)
			{
				s += buff.Buff;
			}

			if (s <= 0)
                s = 1; //No 0 or negative stats, will give errors elsewhere in the code (especially divide by 0 errors).
            return s;
        }

        public bool Update()
        {
            var changed = false;
            var buffs = mBuff.ToArray();
            foreach (var buff in buffs)
            {
                if (buff.Value.Duration <= Globals.Timing.TimeMs)
                {
                    mBuff.Remove(buff.Key);
                    changed = true;
                }
            }

            changed |= mChanged;
            mChanged = false;

            return changed;
        }

        public void AddBuff(EntityBuff buff)
        {
            if (mBuff.ContainsKey(buff.Spell))
            {
                mBuff[buff.Spell].Duration = buff.Duration;
            }
            else
            {
                mBuff.Add(buff.Spell, buff);
            }
            mChanged = true;
        }

        public void Reset()
        {
            mBuff.Clear();
        }
    }

    public class EntityBuff
    {
        public int Buff;
        public long Duration;
        public SpellBase Spell;

        public EntityBuff(SpellBase spell, int buff, int duration)
        {
            Spell = spell;
            Buff = buff;
            Duration = Globals.Timing.TimeMs + duration;
        }
    }

    public class DoTInstance
    {
        private long mInterval;

        public EntityInstance Attacker;

        public int Count;
        public SpellBase SpellBase;
        public EntityInstance Target { get; }

        public DoTInstance(EntityInstance attacker, Guid spellId, EntityInstance target)
        {
            SpellBase = SpellBase.Get(spellId);

            Attacker = attacker;
            Target = target;

            if (SpellBase == null || SpellBase.Combat.HotDotInterval < 1)
            {
                return;
            }

            mInterval = Globals.Timing.TimeMs + SpellBase.Combat.HotDotInterval;
            Count = SpellBase.Combat.Duration / SpellBase.Combat.HotDotInterval - 1;
            target.DoT.Add(this);
            //Subtract 1 since the first tick always occurs when the spell is cast.
        }

        public bool CheckExpired()
        {
            if (Target != null && !Target.DoT.Contains(this)) return false;
            if (SpellBase == null || Count > 0) return false;
            Target?.DoT?.Remove(this);
            return true;
        }

        public void Tick()
        {
            if (CheckExpired()) return;

            if (mInterval > Globals.Timing.TimeMs) return;
            var deadAnimations = new List<KeyValuePair<Guid, sbyte>>();
            var aliveAnimations = new List<KeyValuePair<Guid, sbyte>>();
            if (SpellBase.HitAnimationId != Guid.Empty)
            {
                deadAnimations.Add(new KeyValuePair<Guid, sbyte>(SpellBase.HitAnimationId, (sbyte)Directions.Up));
                aliveAnimations.Add(new KeyValuePair<Guid, sbyte>(SpellBase.HitAnimationId, (sbyte)Directions.Up));
            }

            Attacker?.Attack(Target, SpellBase.Combat.VitalDiff[0], SpellBase.Combat.VitalDiff[1],
                (DamageType)SpellBase.Combat.DamageType, (Stats)SpellBase.Combat.ScalingStat, SpellBase.Combat.Scaling,
                SpellBase.Combat.CritChance, SpellBase.Combat.CritMultiplier, deadAnimations, aliveAnimations);
            mInterval = Globals.Timing.TimeMs + SpellBase.Combat.HotDotInterval;
            Count--;
        }
    }

    public class StatusInstance
    {
        public SpellBase Spell;
        public string Data = "";
        public long Duration;
        private EntityInstance mEntity;
        public long StartTime;
        public StatusTypes Type;
        public int[] shield { get; set; } = new int[(int)Enums.Vitals.VitalCount];

        public StatusInstance(EntityInstance en, SpellBase spell, StatusTypes type, int duration, string data)
        {
            mEntity = en;
            Spell = spell;
            Type = type;
            Duration = Globals.Timing.TimeMs + duration;
            StartTime = Globals.Timing.TimeMs;
            Data = data;

            if (type == StatusTypes.Shield)
            {
                for (int i = (int)Vitals.Health; i < (int)Vitals.VitalCount; i++)
                {
                    if (spell.Combat.VitalDiff[i] > 0)
                        shield[i] = spell.Combat.VitalDiff[i] + ((spell.Combat.Scaling * en.Stat[spell.Combat.ScalingStat].Stat) / 100);
                }
            }

			//If new Cleanse spell, remove all over status effects.
			if (Type == StatusTypes.Cleanse)
			{
				en.Statuses.Clear();
			}
			else
			{
				//If user has a cleanse on, don't add status
				var statuses = en.Statuses.Values.ToArray();
				foreach (var status in statuses)
				{
					if (status.Type == StatusTypes.Cleanse)
					{
						PacketSender.SendActionMsg(en, Strings.Combat.status[(int)Type], CustomColors.Cleanse);
						return;
					}
				}
			}

			if (en.Statuses.ContainsKey(spell))
            {
                en.Statuses[spell].Duration = Duration;
                en.Statuses[spell].StartTime = StartTime;
            }
            else
            {
                en.Statuses.Add(Spell, this);
            }

            PacketSender.SendEntityVitals(mEntity);
        }

        public void TryRemoveStatus()
        {
            if (Duration <= Globals.Timing.TimeMs) //Check the timer
            {
                RemoveStatus();
            }

            //If shield check for out of hp
            if (Type == StatusTypes.Shield)
            {
                for (int i = (int)Vitals.Health; i < (int)Vitals.VitalCount; i++)
                {
                    if (shield[i] > 0) return;
                }
                RemoveStatus();
            }
        }

        public void RemoveStatus()
        {
            mEntity.Statuses.Remove(Spell);
            PacketSender.SendEntityVitals(mEntity);
        }

        public void DamageShield(Vitals vital, ref int amount)
        {
            if (Type == StatusTypes.Shield)
            {
                shield[(int)vital] -= amount;
                if (shield[(int)vital] <= 0)
                {
                    amount = -shield[(int)vital]; //Return piercing damage.
                    shield[(int)vital] = 0;
                    TryRemoveStatus();
                }
                else
                {
                    amount = 0; //Sheild is stronger than the damage dealt, so no piercing damage.
                }
            }
        }
    }

    public class DashInstance
    {
        public byte Direction;
        public int DistanceTraveled;
        public byte Facing;
        public int Range;
        public long TransmittionTimer;

        public DashInstance(EntityInstance en, int range, byte direction, bool blockPass = false, bool activeResourcePass = false, bool deadResourcePass = false, bool zdimensionPass = false)
        {
            DistanceTraveled = 0;
            Direction = direction;
            Facing = (byte)en.Dir;

            CalculateRange(en, range, blockPass, activeResourcePass, deadResourcePass, zdimensionPass);
            if (Range <= 0)
            {
                return;
            } //Remove dash instance if no where to dash
            TransmittionTimer = Globals.Timing.TimeMs + (long)((float)Options.MaxDashSpeed / (float)Range);
            PacketSender.SendEntityDash(en, en.MapId, (byte)en.X, (byte)en.Y, (int)(Options.MaxDashSpeed * (Range / 10f)), Direction == Facing ? (sbyte)Direction : (sbyte)-1);
            en.MoveTimer = Globals.Timing.TimeMs + Options.MaxDashSpeed;
        }

        public void CalculateRange(EntityInstance en, int range, bool blockPass = false, bool activeResourcePass = false, bool deadResourcePass = false, bool zdimensionPass = false)
        {
            var n = 0;
            en.MoveTimer = 0;
            Range = 0;
            for (var i = 1; i <= range; i++)
            {
                n = en.CanMove(Direction);
                if (n == -5) //Check for out of bounds
                {
                    return;
                } //Check for blocks
                if (n == -2 && blockPass == false)
                {
                    return;
                } //Check for ZDimensionTiles
                if (n == -3 && zdimensionPass == false)
                {
                    return;
                } //Check for active resources
                if (n == (int)EntityTypes.Resource && activeResourcePass == false)
                {
                    return;
                } //Check for dead resources
                if (n == (int)EntityTypes.Resource && deadResourcePass == false)
                {
                    return;
                } //Check for players and solid events
                if (n == (int)EntityTypes.Player || n == (int)EntityTypes.Event) return;

                en.Move(Direction, null, true);
                en.Dir = Facing;

                Range = i;
            }
        }
    }
}