/*
 * Copyright (C) 2007 Krzysztof 'DeadwooD' Smiechowicz
 * Original project page: http://sourceforge.net/projects/calindor/
 * 
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 2.1 of the License, or (at your option) any later version.
 */

using System;
using System.Collections.Generic;
using Calindor.Server.Maps;
using Calindor.Server.Messaging;
using Calindor.Misc.Predefines;
using Calindor.Server.Items;
using Calindor.Server;
using Calindor.Server.Resources;

namespace Calindor.Server.TimeBasedActions
{
    public class TimeBasedActionsManager
    {
        private TimeBasedActionList activeActions = new TimeBasedActionList();
        private TimeBasedActionList actionsToRemove = new TimeBasedActionList();

        public void ExecuteActions()
        {
            // Executing actions
            foreach (TimeBasedAction action in activeActions)
            {
                // Execute and check if action finished
                if (!action.Execute())
                    actionsToRemove.Add(action);
            }

            // Removing finished actions
            foreach (TimeBasedAction action in actionsToRemove)
                activeActions.Remove(action);

            actionsToRemove.Clear();
        }

        public void AddAction(ITimeBasedAction action)
        {
            activeActions.Add(action);
        }
    }

    public interface ITimeBasedAction
    {
        /// <summary>
        /// 
        /// </summary>
        /// <returns>False is action finished</returns>
        bool Execute();

        /// <summary>
        /// Causes the action to be canceled
        /// </summary>
        void Cancel();
    }

    public class TimeBasedActionList : List<ITimeBasedAction>
    {
    }

    /// <summary>
    /// Default implementation
    /// </summary>
    public abstract class TimeBasedAction : ITimeBasedAction
    {
        protected EntityImplementation targetEntityImplementation = null;
        protected long lastExecutedTick = DateTime.Now.Ticks;
        protected bool actionCanceled = false;

        protected int getMilisSinceLastExecution()
        {
            return (int)((DateTime.Now.Ticks - lastExecutedTick)) / 10000;
        }

        protected void updateLastExecutionTime()
        {
            lastExecutedTick = DateTime.Now.Ticks;
        }

        public TimeBasedAction(EntityImplementation enImpl)
        {
            if (enImpl == null)
                throw new ArgumentNullException("enImpl");

            targetEntityImplementation = enImpl;
            enImpl.TimeBasedActionSet(this);
        }


        #region ITimeBasedAction Members

        public abstract bool Execute();

        public virtual void Cancel()
        {
            actionCanceled = true;
        }

        #endregion
    }

    public class WalkTimeBasedAction : TimeBasedAction
    {
        protected WalkPath walkPath = null;
        private const int WALK_COMMAND_DELAY = 250; // Delay (in milis) of sending commands
        private bool firstStep = true; 

        public WalkTimeBasedAction(EntityImplementation enImpl, WalkPath walkPath) : base(enImpl)
        {
           if (walkPath == null)
                throw new ArgumentNullException("walkPath");

            this.walkPath = walkPath;

            updateLastExecutionTime();
            firstStep = true;
        }

        public override bool Execute()
        {
            if (actionCanceled)
                return false; // Action canceled. Nothing to do.

            int milisSinceLastExecute = getMilisSinceLastExecution();

            if ((milisSinceLastExecute > WALK_COMMAND_DELAY) || (firstStep))
            {
                // How many moves should be executed
                int numberOfMoves = milisSinceLastExecute / WALK_COMMAND_DELAY;

                // If this is first step
                if (firstStep)
                {
                    numberOfMoves++; // Add one move
                    firstStep = false; // No longer first step
                    walkPath.GetNext(); // Remove the first item (current location) from path 
                    targetEntityImplementation.LocationStandUp(true); // Stand up
                }

                for (int i = 0; i < numberOfMoves; i++)
                {
                    WalkPathItem itm = null;

                    itm = walkPath.GetNext();

                    if (itm == null)
                        return false; //Move finished

                    // Check if location is not occupied
                    if (targetEntityImplementation.LocationCurrentMap.IsLocationOccupied(itm.X, itm.Y))
                        return false; // TODO: Needs to reroute


                    // Check direction
                    int xDiff = targetEntityImplementation.LocationX - itm.X;
                    int yDiff = targetEntityImplementation.LocationY - itm.Y;

                    if (Math.Abs(xDiff) > 1 || Math.Abs(yDiff) > 1)
                        return false; // Error. Stop.

                    if (xDiff == 0)
                    {
                        if (yDiff == -1)
                            targetEntityImplementation.LocationTakeStep(PredefinedDirection.N);

                        if (yDiff == 1)
                            targetEntityImplementation.LocationTakeStep(PredefinedDirection.S);
                    }

                    if (xDiff == -1)
                    {
                        if (yDiff == -1)
                            targetEntityImplementation.LocationTakeStep(PredefinedDirection.NE);

                        if (yDiff == 0)
                            targetEntityImplementation.LocationTakeStep(PredefinedDirection.E);

                        if (yDiff == 1)
                            targetEntityImplementation.LocationTakeStep(PredefinedDirection.SE);
                    }

                    if (xDiff == 1)
                    {
                        if (yDiff == 1)
                            targetEntityImplementation.LocationTakeStep(PredefinedDirection.SW);

                        if (yDiff == 0)
                            targetEntityImplementation.LocationTakeStep(PredefinedDirection.W);

                        if (yDiff == -1)
                            targetEntityImplementation.LocationTakeStep(PredefinedDirection.NW);

                    }
                }

                updateLastExecutionTime();
            }

            return true; // Keep on executing
        }
    }

    public class HarvestTimeBasedAction : TimeBasedAction
    {
        private HarvestableResourceDefinition rscDef = null;
        private double successRate = 0.0;
        private uint harvestTime = 0;

        public HarvestTimeBasedAction(EntityImplementation enImpl, 
            HarvestableResourceDefinition rscDef): base(enImpl)
        {
            if (rscDef == null)
                throw new ArgumentNullException("rscDef");

            this.rscDef = rscDef;
            calculateParameters();
        }

        private void calculateParameters()
        {
            this.successRate = targetEntityImplementation.HarvestGetSuccessRate(rscDef);
            this.harvestTime = targetEntityImplementation.HarvestGetActionTime(rscDef);
        }
        public override void Cancel()
        {
            base.Cancel();
            RawTextOutgoingMessage msgRawText =
                (RawTextOutgoingMessage)OutgoingMessagesFactory.Create(OutgoingMessageType.RAW_TEXT);
            msgRawText.Channel = PredefinedChannel.CHAT_LOCAL;
            msgRawText.Color = PredefinedColor.Blue1;
            msgRawText.Text = "You stoped harvesting " + rscDef.HarvestedItem.Name;
            targetEntityImplementation.PutMessageIntoMyQueue(msgRawText);
        }
        public override bool Execute()
        {
            if (actionCanceled)
                return false; // Action canceled. Nothing to do.

            int milisSinceLastExecute = getMilisSinceLastExecution();

            if (milisSinceLastExecute > harvestTime)
            {
                if (WorldRNG.NextDouble() <= successRate)
                {
                    targetEntityImplementation.HarvestItemHarvested(rscDef);
                }

                // After each harvest, recalculate
                calculateParameters();

                updateLastExecutionTime();
            }

            return true;
        }
    }
}