﻿using System;
using Intersect.Migration.UpgradeInstructions.Upgrade_12.Intersect_Convert_Lib.Collections;

namespace Intersect.Migration.UpgradeInstructions.Upgrade_12.Intersect_Convert_Lib.GameObjects.Maps.MapList
{
    public class MapListMap : MapListItem, IComparable<MapListMap>
    {
        public Guid MapId;
        public long TimeCreated;

        public MapListMap() : base()
        {
            Name = "New Map";
            Type = 1;
        }

        public void PostLoad(DatabaseObjectLookup gameMaps, bool isServer = true)
        {
            if (!isServer)
            {
                if (gameMaps.Keys.Contains(MapId))
                    gameMaps[MapId].Name = Name;
            }
        }

        public int CompareTo(MapListMap obj)
        {
            return TimeCreated.CompareTo(obj.TimeCreated);
        }
    }
}