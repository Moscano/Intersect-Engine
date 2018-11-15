﻿using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Intersect.Migration.UpgradeInstructions.Upgrade_12.Intersect_Convert_Lib.GameObjects.Events
{
    public class EventMoveRoute
    {
        public List<MoveRouteAction> Actions { get; set; } = new List<MoveRouteAction>();
        public bool IgnoreIfBlocked { get; set; }
        public bool RepeatRoute { get; set; }
        public Guid Target { get; set; }

        //Temp Values
        [JsonIgnore]
        public bool Complete { get; set; }
        [JsonIgnore]
        public int ActionIndex { get; set; }

        public EventMoveRoute()
        {
        }

        public void CopyFrom(EventMoveRoute route)
        {
            Target = route.Target;
            Complete = false;
            ActionIndex = 0;
            IgnoreIfBlocked = route.IgnoreIfBlocked;
            RepeatRoute = route.RepeatRoute;
            Actions.Clear();
            foreach (MoveRouteAction action in route.Actions)
            {
                Actions.Add(action.Copy());
            }
        }
    }
}