using Hagar.Invocation;
using System;

namespace CallLog.Scheduling
{
    /// <summary>
    /// Responsible for scheduling incoming messages for an activation.
    /// </summary>
    internal class ActivationMessageScheduler
    {
        private readonly Catalog _catalog;

        /// <summary>
        /// Receive a new message:
        /// - validate order constraints, queue (or possibly redirect) if out of order
        /// - validate transactions constraints
        /// - invoke handler if ready, otherwise enqueue for later invocation
        /// </summary>
        public void ReceiveMessage(IWorkflowContext target, Message message)
        {
            if (message.Body is Response)
            {
                ReceiveResponse(message, target);
            }
            else // Request or OneWay
            {
                ReceiveRequest(message, target);
            }
        }

        /// <summary>
        /// Invoked when an activation has finished a transaction and may be ready for additional transactions
        /// </summary>
        /// <param name="activation">The activation that has just completed processing this message</param>
        /// <param name="message">The message that has just completed processing. 
        /// This will be <c>null</c> for the case of completion of Activate/Deactivate calls.</param>
        internal void OnActivationCompletedRequest(IWorkflowContext activation, Message message)
        {
            lock (activation)
            {
                // Run message pump to see if there is a new request arrived to be processed
                RunMessagePump(activation);
            }
        }

        internal void RunMessagePump(IWorkflowContext activation)
        {
            bool runLoop;
            do
            {
                runLoop = false;
                var nextMessage = activation.PeekNextWaitingMessage();
                if (nextMessage == null)
                {
                    ;
                }

                if (!ActivationMayAcceptRequest(activation, nextMessage))
                {
                    continue;
                }

                activation.DequeueNextWaitingMessage();

                HandleIncomingRequest(nextMessage, activation);
                runLoop = true;
            }
            while (runLoop);
        }

        /// <summary>
        /// Enqueue message for local handling after transaction completes
        /// </summary>
        /// <param name="message"></param>
        /// <param name="targetActivation"></param>
        private void EnqueueRequest(Message message, IWorkflowContext targetActivation)
        {
            targetActivation.EnqueueRequest(message);
        }

        private void ReceiveResponse(Message message, IWorkflowContext targetActivation)
        {
            targetActivation.OnResponse(message);
        }

        /// <summary>
        /// Check if we can locally accept this message.
        /// Redirects if it can't be accepted.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="targetActivation"></param>
        private void ReceiveRequest(Message message, IWorkflowContext targetActivation)
        {
            lock (targetActivation)
            {
                if (!ActivationMayAcceptRequest(targetActivation, message))
                {
                    EnqueueRequest(message, targetActivation);
                }
                else
                {
                    HandleIncomingRequest(message, targetActivation);
                }
            }
        }

        /// <summary>
        /// Determine if the activation is able to currently accept the given message
        /// - always accept responses
        /// For other messages, require that:
        /// - activation is properly initialized
        /// - the message would not cause a reentrancy conflict
        /// </summary>
        /// <param name="targetActivation"></param>
        /// <param name="incoming"></param>
        /// <returns></returns>
        private bool ActivationMayAcceptRequest(IWorkflowContext targetActivation, Message incoming)
        {
            if (!targetActivation.IsCurrentlyExecuting)
            {
                return true;
            }

            /*
            if (incoming.IsAlwaysInterleave)
            {
                return true;
            }

            if (targetActivation.Blocking is null)
            {
                return true;
            }

            if (targetActivation.Blocking.IsReadOnly && incoming.IsReadOnly)
            {
                return true;
            }

            if (targetActivation.GetComponent<GrainCanInterleave>() is GrainCanInterleave canInterleave)
            {
                return canInterleave.MayInterleave(incoming);
            }
            */

            return false;
        }

        /// <summary>
        /// Handle an incoming message and queue/invoke appropriate handler
        /// </summary>
        /// <param name="message"></param>
        /// <param name="targetActivation"></param>
        public void HandleIncomingRequest(Message message, IWorkflowContext targetActivation)
        {
            lock (targetActivation)
            {
                targetActivation.InvokeRequest(message);
            }
        }
    }
}
