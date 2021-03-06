﻿#region Usings

using System;
using System.Threading.Tasks;

using MDD.DataFeed.Fidelity.Models;
using MDD.Library.Abstraction;
using MDD.Library.Abstraction.Client;
using MDD.Library.Abstraction.Manager;
using MDD.Library.Abstraction.Parser;
using MDD.Library.Abstraction.Saver;
using MDD.Library.Helpers;
using MDD.Library.Logging;
using MDD.Library.Models;

#endregion

namespace MDD.DataFeed.Fidelity
{
	public class FidelityManager : DataFeedManagerBase, IDataFeedManager
	{
		private readonly IMyLogger _logger;

		public FidelityManager(IMyLogger logger, IDataFeedClient client, IMarketDataSaver saver, IMarketDataParser parser,
							   IRequestBuilder requestBuilder) : base(client, saver, parser, requestBuilder)
		{
			if (logger == null)
			{
				throw new ArgumentNullException("logger");
			}

			_logger = logger;
			Client.ConnectionEstablished += InvokeConnectionEstablished;
		}

		public Action<bool, string> ConnectionEstablished { get; set; }

		public void StartService()
		{
			CancelationHelper.RearmTokenSource();
			Client.Connect();
		}

		public void StopService()
		{
			CancelationHelper.TokenSource.Cancel();
			Client.Disconnect();
		}

		public async Task DownloadMarketData(RequestBase requestParameters, Parameters programParameters)
		{
			var requestFidelity = (RequestFidelity)requestParameters;

			try
			{
				foreach (var symbol in requestFidelity.Symbols)
				{
					requestFidelity.CurrentSymbol = symbol;
					var requests = RequestBuilder.CreateRequests(requestFidelity);

					var counter = 1;
					foreach (var request in requests)
					{
						_logger.Info(String.Format("Processing {0} [{1}/{2}]", symbol, counter++, requests.Count));

						DataflowHelper.RearmDataflowQueues();

						var downloadTask = new Task(() => Client.StartDownloadingAsync(request));
						var parseTask = new Task(() => Parser.StartParsingAsync(requestFidelity));
						var saveTask = new Task(() => Saver.StartSavingAsync(programParameters, requestFidelity));

						downloadTask.Start();
						parseTask.Start();
						saveTask.Start();

						await
							Task.WhenAll(DataflowHelper.ResponsesQueueCompletion, DataflowHelper.MarketDataQueueCompletion, downloadTask,
										 parseTask, saveTask);
					}
				}
			}
			catch (Exception ex)
			{
				_logger.Error(string.Format("[DownloadMarketData] {0}", ex.Message));
			}
		}

		private void InvokeConnectionEstablished(bool isConnected, string connectionMessage)
		{
			ConnectionEstablished.BeginInvoke(isConnected, connectionMessage, null, null);
		}
	}
}
