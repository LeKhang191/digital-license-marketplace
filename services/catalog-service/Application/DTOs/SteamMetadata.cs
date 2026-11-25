namespace Catalog.Api.Application.DTOs;

public sealed record SteamMetadata(int AppId, string Name, string Description,
    string HeaderImage, string PosterImage, string[] Screenshots, string[] Genres,
    string[] Developers, string[] Publishers, string ReleaseDate,
    string MinimumRequirements, string RecommendedRequirements, string? TrailerUrl);
