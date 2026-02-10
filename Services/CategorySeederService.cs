using BIP_SMEMC.Models;
using System.Linq;

namespace BIP_SMEMC.Services
{
    public class CategorySeederService
    {
        private readonly Supabase.Client _supabase;

        public CategorySeederService(Supabase.Client supabase)
        {
            _supabase = supabase;
        }

        public async Task EnsureIndustriesAndRegionsExist()
        {
            // 1. Exhaustive Industry List
            var targetIndustries = new List<string> {
            "Technology", "Manufacturing", "Retail", "Logistics", "Healthcare",
            "Finance", "Construction", "F&B", "Energy", "Agriculture",
            "Education", "Professional Services", "E-commerce", "Sustainability"
        };

            // 2. Exhaustive Region List (Focus on SME Asia/Global)
            var targetRegions = new List<string> {
            "Singapore", "Malaysia", "Indonesia", "Vietnam", "Thailand",
            "Philippines", "China", "India", "Asia-Pacific", "Global",
            "Europe", "North America"
        };

            // Sync Industries
            var existingIndustries = await _supabase.From<IndustryModel>().Get();
            var industryNames = existingIndustries.Models.Select(m => m.Name).ToList();

            foreach (var name in targetIndustries)
            {
                if (!industryNames.Contains(name))
                {
                    await _supabase.From<IndustryModel>().Insert(new IndustryModel { Name = name });
                }
            }

            // Sync Regions
            var existingRegions = await _supabase.From<RegionModel>().Get();
            var regionNames = existingRegions.Models.Select(m => m.Name).ToList();

            foreach (var name in targetRegions)
            {
                if (!regionNames.Contains(name))
                {
                    await _supabase.From<RegionModel>().Insert(new RegionModel { Name = name });
                }
            }
        }
    }
}
