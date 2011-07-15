﻿namespace Tvl.VisualStudio.Language.Parsing.Experimental.Interpreter
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using Tvl.VisualStudio.Language.Parsing.Experimental.Atn;

    [DebuggerDisplay("Transition Count: {Transitions.Count}")]
    public class InterpretTrace : IEquatable<InterpretTrace>
    {
        private static readonly LinkedList<InterpretTraceTransition> EmptyTransitions = new LinkedList<InterpretTraceTransition>();

        public readonly ContextFrame StartContext;
        public readonly LinkedList<InterpretTraceTransition> Transitions = new LinkedList<InterpretTraceTransition>();
        public readonly ContextFrame EndContext;
        public readonly bool BoundedStart;
        public readonly bool BoundedEnd;

        public InterpretTrace(ContextFrame startContext, ContextFrame endContext)
            : this(startContext, endContext, EmptyTransitions, false, false, false)
        {
        }

        private InterpretTrace(ContextFrame startContext, ContextFrame endContext, LinkedList<InterpretTraceTransition> transitions, bool boundedStart, bool boundedEnd, bool currentBounded)
        {
            StartContext = startContext;
            EndContext = endContext;
            if (currentBounded)
                Transitions = transitions;
            else
                Transitions = new LinkedList<InterpretTraceTransition>(transitions);
            BoundedStart = boundedStart;
            BoundedEnd = boundedEnd;
        }

        public Network Network
        {
            get
            {
                return StartContext.Network;
            }
        }

        public NetworkInterpreter Interpreter
        {
            get
            {
                return StartContext.Interpreter;
            }
        }

        public bool TryStepBackward(Transition transition, int symbol, int symbolPosition, out InterpretTrace result)
        {
            Contract.Requires<ArgumentNullException>(transition != null, "transition");

            bool boundedStart = BoundedStart;
            if (!boundedStart && Transitions.Count > 0 && Interpreter.BoundaryStates.Contains(Transitions.First.Value.Transition.SourceState))
            {
                bool nested = false;
                for (ContextFrame parent = StartContext.Parent; parent != null; parent = parent.Parent)
                {
                    if (parent.Context != null)
                    {
                        string contextRule = Network.StateRules[parent.Context.Value];
                        RuleBinding ruleBinding = Network.GetRule(contextRule);
                        if (Interpreter.BoundaryStates.Contains(ruleBinding.StartState))
                            nested = true;
                    }
                }

                boundedStart = !nested;
            }

            result = null;

            if (transition.IsMatch)
            {
                if (!transition.MatchSet.Contains(symbol))
                    return false;

                ContextFrame startContext = new ContextFrame(transition.SourceState, this.StartContext.Context, this.StartContext.Parent, Interpreter);
                result = new InterpretTrace(startContext, this.EndContext, this.Transitions, boundedStart, this.BoundedEnd, boundedStart);
                if (!boundedStart)
                    result.Transitions.AddFirst(new InterpretTraceTransition(transition, symbol, symbolPosition, Interpreter));

                return true;
            }

            if (!transition.SourceState.GetSourceSet().Contains(symbol))
                return false;

            if (transition.IsContext)
            {
                PopContextTransition popContextTransition = transition as PopContextTransition;
                if (popContextTransition != null)
                {
                    ContextFrame subContext = new ContextFrame(popContextTransition.SourceState, popContextTransition.ContextIdentifier, this.StartContext, Interpreter);
                    result = new InterpretTrace(subContext, this.EndContext, this.Transitions, boundedStart, this.BoundedEnd, boundedStart);
                    if (!boundedStart)
                        result.Transitions.AddFirst(new InterpretTraceTransition(transition, Interpreter));

                    return true;
                }

                PushContextTransition pushContextTransition = transition as PushContextTransition;
                if (pushContextTransition != null)
                {
                    ContextFrame startContext = this.StartContext;
                    ContextFrame endContext = this.EndContext;

                    if (startContext.Context != null)
                    {
                        // if the start context has a state stack, pop an item off it
                        if (!string.Equals(startContext.Context, pushContextTransition.ContextIdentifier))
                            return false;

                        startContext = new ContextFrame(transition.SourceState, startContext.Parent.Context, startContext.Parent.Parent, Interpreter);
                    }
                    else
                    {
                        // else we add a "predicate" to the end context
                        endContext = endContext.AddHeadContext(new ContextFrame(Network.States[pushContextTransition.ContextIdentifier], pushContextTransition.ContextIdentifier, null, Interpreter));
                        startContext = new ContextFrame(transition.SourceState, startContext.Context, startContext.Parent, Interpreter);
                    }

                    result = new InterpretTrace(startContext, endContext, this.Transitions, boundedStart, this.BoundedEnd, boundedStart);
                    if (!boundedStart)
                        result.Transitions.AddFirst(new InterpretTraceTransition(transition, Interpreter));

                    return true;
                }

                throw new NotSupportedException("Unknown context transition.");
            }
            else if (transition.IsEpsilon)
            {
                ContextFrame startContext = new ContextFrame(transition.SourceState, this.StartContext.Context, this.StartContext.Parent, Interpreter);

                bool addTransition = !boundedStart && Interpreter.BoundaryStates.Contains(transition.SourceState);
                result = new InterpretTrace(startContext, this.EndContext, this.Transitions, boundedStart, this.BoundedEnd, !addTransition);

                if (addTransition)
                    result.Transitions.AddFirst(new InterpretTraceTransition(transition, Interpreter));

                return true;
            }

            throw new NotSupportedException("Unknown transition type.");
        }

        public bool TryStepForward(Transition transition, int symbol, int symbolPosition, out InterpretTrace result)
        {
            Contract.Requires<ArgumentNullException>(transition != null, "transition");

            bool boundedEnd = BoundedEnd;
            if (!boundedEnd && Transitions.Count > 0 && Interpreter.ForwardBoundaryStates.Contains(Transitions.Last.Value.Transition.TargetState))
                boundedEnd = true;

            result = null;

            if (transition.IsMatch)
            {
                if (!transition.MatchSet.Contains(symbol))
                    return false;

                ContextFrame endContext = new ContextFrame(transition.TargetState, this.EndContext.Context, this.EndContext.Parent, Interpreter);
                result = new InterpretTrace(this.StartContext, endContext, this.Transitions, this.BoundedStart, boundedEnd, boundedEnd);
                if (!boundedEnd)
                    result.Transitions.AddLast(new InterpretTraceTransition(transition, symbol, symbolPosition, Interpreter));

                return true;
            }

            if (!transition.TargetState.GetFollowSet().Contains(symbol))
                return false;

            if (transition.IsContext)
            {
                PushContextTransition pushContextTransition = transition as PushContextTransition;
                if (pushContextTransition != null)
                {
                    ContextFrame subContext = new ContextFrame(pushContextTransition.TargetState, pushContextTransition.ContextIdentifier, this.EndContext, Interpreter);
                    result = new InterpretTrace(this.StartContext, subContext, this.Transitions, this.BoundedStart, boundedEnd, boundedEnd);
                    if (!boundedEnd)
                        result.Transitions.AddLast(new InterpretTraceTransition(transition, Interpreter));

                    return true;
                }

                PopContextTransition popContextTransition = transition as PopContextTransition;
                if (popContextTransition != null)
                {
                    ContextFrame startContext = this.StartContext;
                    ContextFrame endContext = this.EndContext;

                    if (endContext.Context != null)
                    {
                        // if the end context has a state stack, pop an item off it
                        if (!string.Equals(endContext.Context, popContextTransition.ContextIdentifier))
                            return false;

                        endContext = new ContextFrame(transition.TargetState, endContext.Parent.Context, endContext.Parent.Parent, Interpreter);
                    }
                    else
                    {
                        // else we add a "predicate" to the start context
                        startContext = startContext.AddHeadContext(new ContextFrame(Network.States[popContextTransition.ContextIdentifier], popContextTransition.ContextIdentifier, null, Interpreter));
                        endContext = new ContextFrame(transition.TargetState, endContext.Context, endContext.Parent, Interpreter);
                    }

                    result = new InterpretTrace(startContext, endContext, this.Transitions, this.BoundedStart, boundedEnd, boundedEnd);
                    if (!boundedEnd)
                        result.Transitions.AddLast(new InterpretTraceTransition(transition, Interpreter));

                    return true;
                }

                throw new NotSupportedException("Unknown context transition.");
            }
            else if (transition.IsEpsilon)
            {
                ContextFrame endContext = new ContextFrame(transition.TargetState, this.EndContext.Context, this.EndContext.Parent, Interpreter);
                result = new InterpretTrace(this.StartContext, endContext, this.Transitions, this.BoundedStart, boundedEnd, boundedEnd);
                if (!boundedEnd && Interpreter.ForwardBoundaryStates.Contains(transition.TargetState))
                    result.Transitions.AddLast(new InterpretTraceTransition(transition, Interpreter));

                return true;
            }

            throw new NotSupportedException("Unknown transition type.");
        }

        public virtual bool Equals(InterpretTrace other)
        {
            if (other == null)
                return false;

            return StartContext.Equals(other.StartContext)
                && EndContext.Equals(other.EndContext)
                && Transitions.SequenceEqual(other.Transitions);
        }

        public sealed override bool Equals(object obj)
        {
            return Equals(obj as InterpretTrace);
        }

        public override int GetHashCode()
        {
            return StartContext.GetHashCode() ^ EndContext.GetHashCode() /*^ Transitions.Aggregate(0, (i, j) => i ^ j.GetHashCode())*/;
        }
    }
}
