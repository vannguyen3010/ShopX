﻿using Entities.Models;
using Shared;

namespace Contracts
{
    public interface IBannerProductRepository
    {
        Task<BannerProduct> CreateBannerProductAsync(BannerProduct banner);
        Task<IEnumerable<BannerProduct>> GetAllBannerProductAsync(bool trackChanges);

        Task<BannerProduct> GetBannerProductbyId(Guid brandId, bool trackChanges);
        void UpdateBannerProduct(BannerProduct banner);
        void DeleteBannerProduct(BannerProduct banner);
    }
}
