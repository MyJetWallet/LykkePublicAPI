﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Core.Feed;
using Core.Services;
using Lykke.Domain.Prices;
using Lykke.Service.Assets.Client;
using LykkePublicAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Lykke.Service.CandlesHistory.Client;
using Lykke.Service.CandlesHistory.Client.Models;

namespace LykkePublicAPI.Controllers
{
    [Route("api/[controller]")]
    public class AssetPairsController : Controller
    {
        private readonly IAssetsServiceWithCache _assetsServiceWithCache;
        private readonly ICandlesHistoryServiceProvider _candlesServiceProvider;
        private readonly IFeedHistoryRepository _feedHistoryRepository;
        private readonly IMarketProfileService _marketProfileService;

        public AssetPairsController(
            IAssetsServiceWithCache assetsServiceWithCache,
            ICandlesHistoryServiceProvider candlesServiceProvider, 
            IFeedHistoryRepository feedHistoryRepository,
            IMarketProfileService marketProfileService)
        {
            _assetsServiceWithCache = assetsServiceWithCache;
            _candlesServiceProvider = candlesServiceProvider;
            _feedHistoryRepository = feedHistoryRepository;
            _marketProfileService = marketProfileService;
        }

        /// <summary>
        /// Get all asset pairs rates
        /// </summary>
        [HttpGet("rate")]
        public async Task<IEnumerable<ApiAssetPairRateModel>> GetRate()
        {
            var assetPairsIds = (await _assetsServiceWithCache.GetAllAssetPairsAsync())
                .Where(x => !x.IsDisabled)
                .Select(x => x.Id)
                .ToArray();

            var marketProfile = (await _marketProfileService.GetAllPairsAsync())
                .Where(x => assetPairsIds.Contains(x.AssetPair))
                .Select(pair => pair.ToApiModel());

            return marketProfile;
        }

        /// <summary>
        /// Get rates for asset pair
        /// </summary>
        [HttpGet("rate/{assetPairId}")]
        public async Task<ApiAssetPairRateModel> GetRate(string assetPairId)
        {
            return (await _marketProfileService.TryGetPairAsync(assetPairId))?.ToApiModel();
        }

        /// <summary>
        /// Get asset pairs dictionary
        /// </summary>
        [HttpGet("dictionary/{market?}")]
        public async Task<IEnumerable<ApiAssetPair>> GetDictionary([FromRoute] MarketType? market)
        {
            var pairs = (await _assetsServiceWithCache.GetAllAssetPairsAsync()).Where(x => !x.IsDisabled);

            //for now for MT we display only asset pairs configured in candles
            if (market == MarketType.Mt)
            {
                var mtPairs = await _candlesServiceProvider.Get(Core.Domain.Market.MarketType.Mt)
                    .GetAvailableAssetPairsAsync();

                pairs = pairs.Where(p => mtPairs.Contains(p.Id));
            }

            return pairs.ToApiModel();
        }

        /// <summary>
        /// Get rates for specified period
        /// </summary>
        /// <remarks>
        /// Available period values
        ///  
        ///     "Sec",
        ///     "Minute",
        ///     "Hour",
        ///     "Day",
        ///     "Month",
        /// 
        /// </remarks>
        [HttpPost("rate/history")]
        [ProducesResponseType(typeof(IEnumerable<ApiAssetPairRateModel>), 200)]
        [ProducesResponseType(typeof(ApiError), 400)]
        public async Task<IActionResult> GetHistoryRate([FromBody] AssetPairsRateHistoryRequest request)
        {
            //if (request.AssetPairIds.Length > 10)
            //    return
            //        BadRequest(new ApiError {Code = ErrorCodes.InvalidInput, Msg = "Maximum 10 asset pairs allowed" });

            if (request.Period != Period.Day)
                return
                    BadRequest(new ApiError { Code = ErrorCodes.InvalidInput, Msg = "Sorry, only day candles are available (temporary)." });

            var pairs = (await _assetsServiceWithCache.GetAllAssetPairsAsync()).Where(x => !x.IsDisabled);

            if (request.AssetPairIds.Any(x => !pairs.Select(y => y.Id).Contains(x)))
                return
                    BadRequest(new ApiError {Code = ErrorCodes.InvalidInput, Msg = "Unkown asset pair id present"});

            //var candlesTasks = new List<Task<CandleWithPairId>>();

            var candles = new List<CandleWithPairId>();
            var result = new List<ApiAssetPairHistoryRateModel>();

            foreach (var pairId in request.AssetPairIds)
            {
                var askFeed = _feedHistoryRepository.GetСlosestAvailableAsync(pairId, TradePriceType.Ask, request.DateTime);
                var bidFeed = _feedHistoryRepository.GetСlosestAvailableAsync(pairId, TradePriceType.Bid, request.DateTime);

                var askCandle = (await askFeed)?.ToCandleWithPairId();
                var bidCandle = (await bidFeed)?.ToCandleWithPairId();

                if (askCandle != null && bidCandle != null)
                {
                    candles.Add(askCandle);
                    candles.Add(bidCandle);
                }
                else
                {
                    //add empty candles
                    result.Add(new ApiAssetPairHistoryRateModel {Id = pairId});
                }

                //candlesTasks.Add(_candlesHistoryService.ReadCandleAsync(pairId, request.Period.ToCandlesHistoryServiceModel(),
                //    true, request.DateTime).ContinueWith(task => new CandleWithPairId
                //{
                //    AssetPairId = pairId,
                //    Candle = task.Result
                //}));

                //candlesTasks.Add(_candlesHistoryService.ReadCandleAsync(pairId, request.Period.ToCandlesHistoryServiceModel(),
                //    false, request.DateTime).ContinueWith(task => new CandleWithPairId
                //{
                //    AssetPairId = pairId,
                //    Candle = task.Result
                //}));
            }

            //var candles = await Task.WhenAll(candlesTasks);

            result.AddRange(candles.ToApiModel());

            return Ok(result);
        }


        /// <summary>
        /// Get rates for specified period and asset pair
        /// </summary>
        /// <remarks>
        /// Available period values
        ///  
        ///     "Sec",
        ///     "Minute",
        ///     "Hour",
        ///     "Day",
        ///     "Month",
        /// 
        /// </remarks>
        /// <param name="assetPairId">Asset pair Id</param>
        /// <param name="request">Request model</param>
        [HttpPost("rate/history/{assetPairId}")]
        public async Task<ApiAssetPairHistoryRateModel> GetHistoryRate([FromRoute]string assetPairId,
            [FromBody] AssetPairRateHistoryRequest request)
        {
            var timeInterval = request.Period.ToDomainModel();
            // HACK: Day and month ticks are starts from 1, AddIntervalTicks takes this into account,
            // so compensate it here
            var toDate = timeInterval == TimeInterval.Day || timeInterval == TimeInterval.Month
                ? request.DateTime.AddIntervalTicks(2, timeInterval)
                : request.DateTime.AddIntervalTicks(1, timeInterval);

            var candlesHistoryService = _candlesServiceProvider.Get(Core.Domain.Market.MarketType.Spot);
            
            var buyHistory = await candlesHistoryService.GetCandlesHistoryAsync(assetPairId, CandlePriceType.Bid, request.Period.ToCandlesHistoryServiceApiModel(), request.DateTime, toDate);
            var sellHistory = await candlesHistoryService.GetCandlesHistoryAsync(assetPairId, CandlePriceType.Ask, request.Period.ToCandlesHistoryServiceApiModel(), request.DateTime, toDate);
            var tradeHistory = await candlesHistoryService.GetCandlesHistoryAsync(assetPairId, CandlePriceType.Trades, request.Period.ToCandlesHistoryServiceApiModel(), request.DateTime, toDate);

            var buyCandle = buyHistory.History.SingleOrDefault();
            var sellCandle = sellHistory.History.SingleOrDefault();
            var tradeCandle = tradeHistory.History.SingleOrDefault();

            return Convertions.ToApiModel(assetPairId, buyCandle, sellCandle, tradeCandle);
        }
    }
}
