﻿using System;
using Newtonsoft.Json;

namespace Intersect.Migration.UpgradeInstructions.Upgrade_12.Intersect_Convert_Lib.GameObjects.Switches_and_Variables
{
    public class ServerSwitchBase : DatabaseObject<ServerSwitchBase>
    {
        //Identifier used for event chat variables to display the value of this variable/switch.
        //See https://www.ascensiongamedev.com/topic/749-event-text-variables/ for usage info.
        public string TextId { get; set; }

        public bool Value { get; set; }

        [JsonConstructor]
        public ServerSwitchBase(Guid id) : base(id)
        {
            Name = "New Global Switch";
        }

        public ServerSwitchBase()
        {
            Name = "New Global Switch";
        }
    }
}