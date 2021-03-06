﻿using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Core;
using Core.Services;
using Lykke.Service.Assets.Client;
using Lykke.Service.Assets.Client.Models.Extensions;
using Services.NinjaContracts;

namespace Services
{
    /// <summary>
    /// The class responsible for communication with Bitcoin Ninja server.
    /// </summary>
    public class NinjaNetworkClient : INinjaNetworkClient
    {
        private readonly IAssetsServiceWithCache _assetsServiceWithCache;
        private readonly string _url;

        #region Initialization

        public NinjaNetworkClient(IAssetsServiceWithCache assetsServiceWithCache, string url)
        {
            _assetsServiceWithCache = assetsServiceWithCache ?? throw new ArgumentNullException(nameof(assetsServiceWithCache));

            _url = 
                !string.IsNullOrWhiteSpace(url)
                ? url
                : throw new ArgumentNullException(nameof(url));
        }

        #endregion

        #region Public

        /// <inheritdoc cref="INinjaNetworkClient"/>
        public async Task<double> GetBalanceAsync(string address, string assetName)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentNullException(nameof(address));
            if (string.IsNullOrWhiteSpace(assetName))
                throw new ArgumentNullException(nameof(assetName));

            var asset = await _assetsServiceWithCache.TryGetAssetAsync(assetName);
            if (asset == null)
                throw new ArgumentException($"There is no such an asset: {assetName}"); // Only exception, only hardcore :)

            var btc = await _assetsServiceWithCache.TryGetAssetAsync(LykkeConstants.BitcoinAssetId);

            var completeUrl = new Uri(new Uri(_url), $"balances/{address}/summary?colored=true");
            var response = await ExecuteRequestAsync<BalanceSummaryModel>(completeUrl.AbsoluteUri);

            // Check if they asked for BTC balance. If so, we can go by a short circuit.
            if (asset.BlockChainAssetId == null)
                return response.Spendable.Amount * btc.Multiplier();

            var balanceSummaryForAsset =
                response.Spendable.Assets.FirstOrDefault(a => a.AssetId == asset.BlockChainAssetId);

            return (balanceSummaryForAsset?.Quantity ?? 0) * asset.Multiplier();
        }

        #endregion

        #region Private

        // May throw exception in case of network problems or data corruption (when unable to decerialize).
        // The possible exception should be handled in the caller code.
        private static async Task<T> ExecuteRequestAsync<T>(string request)
        {
            var webRequest = (HttpWebRequest) WebRequest.Create(request);
            webRequest.Method = "GET";
            webRequest.ContentType = "application/x-www-form-urlencoded";

            string resultString;

            using (var webResponse = await webRequest.GetResponseAsync())
            {
                using (var recieveStream = webResponse.GetResponseStream())
                {
                    using (var sr = new StreamReader(recieveStream))
                    {
                        resultString = await sr.ReadToEndAsync();
                    }
                }
            }

            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(resultString);
        }

        #endregion
    }
}
