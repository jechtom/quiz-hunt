﻿using Microsoft.AspNetCore.SignalR;
using QuizHive.Server.DataLayer;
using QuizHive.Server.Hubs;
using QuizHive.Server.Hubs.Messages;
using QuizHive.Server.State;

namespace QuizHive.Server.Services
{
    public class SessionActionDispatcher
    {
        private readonly ILogger<SessionActionDispatcher> logger;
        private readonly IRepository<Session> sessionRepository;
        private readonly IHubContext<AppHub> hub;

        public SessionActionDispatcher(
            ILogger<SessionActionDispatcher> logger,
            IRepository<Session> sessionRepository,
            IHubContext<AppHub> hub)
        {
            this.logger = logger;
            this.sessionRepository = sessionRepository;
            this.hub = hub;
        }

        public async Task<(bool, Session?)> TryDispatchActionAsync(string sessionId, Func<Session, Session> updateAction)
        {
            Session? sessionUpdated;

            for (int retry = 0;; retry++)
            {
                if(retry > 50)
                {
                    // failsafe to prevent infinite loop
                    throw new InvalidOperationException("Can't save. Too many retries.");
                }

                (bool found, Session? sessionOriginal, VersionKey version) = await sessionRepository.TryGetAsync(sessionId);

                if (!found)
                {
                    throw new InvalidOperationException("Session not found.");
                }

                sessionUpdated = updateAction(sessionOriginal ?? throw new InvalidOperationException());

                if (sessionOriginal == sessionUpdated)
                {
                    return (false, default); // no change
                }

                (bool success, VersionKey newVersionKey) = await sessionRepository.TrySetAsync(sessionUpdated.SessionId, sessionUpdated, version);

                if(success)
                {
                    break;
                }
            }

            // distribute new state to connected clients
            foreach (var player in sessionUpdated.Players.Values.Where(p => !p.IsDisconnected))
            {
                var state = sessionUpdated.ResolveStateForClient(player);
                await hub.Clients.Client(player.Id).SendAsync(MessageCodes.SessionStateUpdate, state);
            }

            return (true, sessionUpdated);
        }
    }
}
