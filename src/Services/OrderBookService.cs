﻿using System.Collections.Generic;
using System.Threading.Tasks;
using Common;
using Core.Domain.Settings;
using Core.Services;
using Microsoft.Extensions.Caching.Distributed;
using Lykke.Domain.Prices.Contracts;
using Lykke.Domain.Prices.Model;
using Lykke.Service.Assets.Client;

namespace Services
{
    public class OrderBookService : IOrderBooksService
    {
        private readonly IDistributedCache _distributedCache;
        private readonly PublicApiSettings _settings;
        private readonly IAssetsServiceWithCache _assetsServiceWithCache;

        public OrderBookService(IDistributedCache distributedCache,
            PublicApiSettings settings,
            IAssetsServiceWithCache assetsServiceWithCache)
        {
            _distributedCache = distributedCache;
            _settings = settings;
            _assetsServiceWithCache = assetsServiceWithCache;
        }

        public async Task<IEnumerable<IOrderBook>> GetAllAsync()
        {
            var assetPairs = await _assetsServiceWithCache.GetAllAssetPairsAsync();
            var orderBooks = new List<IOrderBook>();

            foreach (var pair in assetPairs)
            {
                var buyBookJson = _distributedCache.GetStringAsync(_settings.CacheSettings.GetOrderBookKey(pair.Id, true));
                var sellBookJson = _distributedCache.GetStringAsync(_settings.CacheSettings.GetOrderBookKey(pair.Id, false));

                var buyBook = (await buyBookJson)?.DeserializeJson<OrderBook>();
                if (buyBook != null)
                    orderBooks.Add(buyBook);

                var sellBook = (await sellBookJson)?.DeserializeJson<OrderBook>();
                if (sellBook != null)
                    orderBooks.Add(sellBook);
            }

            return orderBooks;
        }

        public async Task<IEnumerable<IOrderBook>> GetAsync(string assetPairId)
        {
            var sellBook = _distributedCache.GetStringAsync(_settings.CacheSettings.GetOrderBookKey(assetPairId, false));
            var buyBook = _distributedCache.GetStringAsync(_settings.CacheSettings.GetOrderBookKey(assetPairId, true));
            return new IOrderBook[] { (await sellBook)?.DeserializeJson<OrderBook>(),
                (await buyBook)?.DeserializeJson<OrderBook>()};
        }
    }
}
