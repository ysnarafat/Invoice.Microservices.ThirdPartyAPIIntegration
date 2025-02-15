﻿using System;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Communication.Sender;
using Integration.Likvido;
using InvoiceProcessor.Application.Factories;
using InvoiceProcessor.Domain.Enums;
using InvoiceProcessor.Infrastructure.Persistence;
using MediatR;
using InvoiceProcessor.Messages;
using InvoiceReader.Messages;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace InvoiceProcessor.Application.Commands.SendInvoice
{
    public partial class SendInvoice
    {
        public class CommandHandler : AsyncRequestHandler<SendInvoiceCommand>
        {
            private readonly ILikvidoClient _likvidoClient;
            private readonly IMediator _mediator;
            private readonly IUnitOfWork _unitOfWork;
            private readonly ILogger<CommandHandler> _logger;
            private readonly IOutBoxFactory _outBoxFactory;
            private readonly IDistributedSender _distributedSender;

            public CommandHandler(ILikvidoClient likvidoClient, 
                IMediator mediator, 
                IUnitOfWork unitOfWork, 
                ILogger<CommandHandler> logger, 
                IOutBoxFactory outBoxFactory,
                IDistributedSender distributedSender)
            {
                _likvidoClient = likvidoClient;
                _mediator = mediator;
                _unitOfWork = unitOfWork;
                _logger = logger;
                _outBoxFactory = outBoxFactory;
                _distributedSender = distributedSender;
            }

            protected override async Task Handle(SendInvoiceCommand request, CancellationToken cancellationToken)
            {
                var outBoxCommand = _outBoxFactory.GetOutBoxEvent(OutBoxStatus.Processed, guid: request.MessageId);

                var payload = JsonConvert.SerializeObject(request);

                await _unitOfWork.RunInTransactionAsync(async () =>
                {
                    await _mediator.Send(outBoxCommand, cancellationToken);

                    using var transaction = new TransactionScope(TransactionScopeOption.Required,
                        TransactionScopeAsyncFlowOption.Enabled);
                    await _distributedSender.SendMessageAsync(new NewInvoiceAdded(request.MessageId));

                    var clientResponse = await _likvidoClient.SendInvoiceAsync(payload, cancellationToken);

                    if (!clientResponse.IsSuccess)
                    {
                        _logger.LogError(clientResponse.Message);
                        throw new Exception(clientResponse.Message);
                    }

                    transaction.Complete();
                }, cancellationToken);
            }
        }
    }
}
