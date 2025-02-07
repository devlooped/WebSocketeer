﻿// <autogenerated />
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Devlooped.Sponsors;

static partial class SponsorLink
{
    public record StatusOptions(ImmutableArray<AdditionalText> AdditionalFiles, AnalyzerConfigOptions GlobalOptions);

    /// <summary>
    /// Statically cached dictionary of sponsorable accounts and their public key (in JWK format), 
    /// retrieved from assembly metadata attributes starting with "Funding.GitHub.".
    /// </summary>
    public static Dictionary<string, string> Sponsorables { get; } = typeof(SponsorLink).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Where(x => x.Key.StartsWith("Funding.GitHub."))
        .Select(x => new { Key = x.Key[15..], x.Value })
        .ToDictionary(x => x.Key, x => x.Value);

    /// <summary>
    /// Whether the current process is running in an IDE, either 
    /// <see cref="IsVisualStudio"/> or <see cref="IsRider"/>.
    /// </summary>
    public static bool IsEditor => IsVisualStudio || IsRider;

    /// <summary>
    /// Whether the current process is running as part of an active Visual Studio instance.
    /// </summary>
    public static bool IsVisualStudio =>
        Environment.GetEnvironmentVariable("ServiceHubLogSessionKey") != null ||
        Environment.GetEnvironmentVariable("VSAPPIDNAME") != null;

    /// <summary>
    /// Whether the current process is running as part of an active Rider instance.
    /// </summary>
    public static bool IsRider =>
        Environment.GetEnvironmentVariable("RESHARPER_FUS_SESSION") != null ||
        Environment.GetEnvironmentVariable("IDEA_INITIAL_DIRECTORY") != null;

    /// <summary>
    /// A unique session ID associated with the current IDE or process running the analyzer.
    /// </summary>
    public static string SessionId => 
        IsVisualStudio ? Environment.GetEnvironmentVariable("ServiceHubLogSessionKey") :
        IsRider ? Environment.GetEnvironmentVariable("RESHARPER_FUS_SESSION") :
        Process.GetCurrentProcess().Id.ToString();

    /// <summary>
    /// Manages the sharing and reporting of diagnostics across the source generator 
    /// and the diagnostic analyzer, to avoid doing the online check more than once.
    /// </summary>
    public static DiagnosticsManager Diagnostics { get; } = new();

    /// <summary>
    /// Gets the expiration date from the principal, if any.
    /// </summary>
    /// <returns>
    /// Whichever "exp" claim is the latest, or <see langword="null"/> if none found.
    /// </returns>
    public static DateTime? GetExpiration(this ClaimsPrincipal principal)
        // get all "exp" claims, parse them and return the latest one or null if none found
        => principal.FindAll("exp")
            .Select(c => c.Value)
            .Select(long.Parse)
            .Select(DateTimeOffset.FromUnixTimeSeconds)
            .Max().DateTime is var exp && exp == DateTime.MinValue ? null : exp;

    /// <summary>
    /// Gets all necessary additional files to determine status.
    /// </summary>
    public static ImmutableArray<AdditionalText> GetSponsorAdditionalFiles(this AnalyzerOptions? options) 
        => options == null ? ImmutableArray.Create<AdditionalText>() : options.AdditionalFiles
            .Where(x => x.IsSponsorManifest(options.AnalyzerConfigOptionsProvider) || x.IsSponsorableAnalyzer(options.AnalyzerConfigOptionsProvider))
            .ToImmutableArray();

    /// <summary>
    /// Gets all sponsor manifests from the provided analyzer options.
    /// </summary>
    public static IncrementalValueProvider<ImmutableArray<AdditionalText>> GetSponsorAdditionalFiles(this IncrementalGeneratorInitializationContext context)
        => context.AdditionalTextsProvider.Combine(context.AnalyzerConfigOptionsProvider)
            .Where(source =>
            {
                var (text, provider) = source;
                return text.IsSponsorManifest(provider) || text.IsSponsorableAnalyzer(provider);
            })
            .Select((source, c) => source.Left)
            .Collect();

    /// <summary>
    /// Gets the status options for use within an incremental generator, to avoid depending on 
    /// analyzer runs. Used in combination with <see cref="DiagnosticsManager.GetOrSetStatus(StatusOptions)"/>.
    /// </summary>
    public static IncrementalValueProvider<StatusOptions> GetStatusOptions(this IncrementalGeneratorInitializationContext context)
        => context.GetSponsorAdditionalFiles().Combine(context.AnalyzerConfigOptionsProvider)
            .Select((source, _) => new StatusOptions(source.Left, source.Right.GlobalOptions));

    /// <summary>
    /// Gets the status options for use within a source generator, to avoid depending on 
    /// analyzer runs. Used in combination with <see cref="DiagnosticsManager.GetOrSetStatus(StatusOptions)"/>.
    /// </summary>
    public static StatusOptions GetStatusOptions(this GeneratorExecutionContext context)
        => new StatusOptions(
            context.AdditionalFiles.Where(x => x.IsSponsorManifest(context.AnalyzerConfigOptions) || x.IsSponsorableAnalyzer(context.AnalyzerConfigOptions)).ToImmutableArray(),
            context.AnalyzerConfigOptions.GlobalOptions);

    static bool IsSponsorManifest(this AdditionalText text, AnalyzerConfigOptionsProvider provider)
        => provider.GetOptions(text).TryGetValue("build_metadata.SponsorManifest.ItemType", out var itemType) &&
           itemType == "SponsorManifest" &&
           Sponsorables.ContainsKey(Path.GetFileNameWithoutExtension(text.Path));
    
    static bool IsSponsorableAnalyzer(this AdditionalText text, AnalyzerConfigOptionsProvider provider)
        => provider.GetOptions(text) is { } options && 
           options.TryGetValue("build_metadata.Analyzer.ItemType", out var itemType) &&
           options.TryGetValue("build_metadata.Analyzer.NuGetPackageId", out var packageId) &&
           itemType == "Analyzer" &&
           Funding.PackageIds.Contains(packageId);

    /// <summary>
    /// Reads all manifests, validating their signatures.
    /// </summary>
    /// <param name="principal">The combined principal with all identities (and their claims) from each provided and valid JWT</param>
    /// <param name="values">The tokens to read and their corresponding JWK for signature verification.</param>
    /// <returns><see langword="true"/> if at least one manifest can be successfully read and is valid. 
    /// <see langword="false"/> otherwise.</returns>
    public static bool TryRead([NotNullWhen(true)] out ClaimsPrincipal? principal, params (string jwt, string jwk)[] values)
        => TryRead(out principal, values.AsEnumerable());

    /// <summary>
    /// Reads all manifests, validating their signatures.
    /// </summary>
    /// <param name="principal">The combined principal with all identities (and their claims) from each provided and valid JWT</param>
    /// <param name="values">The tokens to read and their corresponding JWK for signature verification.</param>
    /// <returns><see langword="true"/> if at least one manifest can be successfully read and is valid. 
    /// <see langword="false"/> otherwise.</returns>
    public static bool TryRead([NotNullWhen(true)] out ClaimsPrincipal? principal, IEnumerable<(string jwt, string jwk)> values)
    {
        principal = null;

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value.jwt) || string.IsNullOrEmpty(value.jwk))
                continue;

            if (Validate(value.jwt, value.jwk, out var token, out var identity, false) == ManifestStatus.Valid && identity != null)
            {
                if (principal == null)
                    principal = new JwtRolesPrincipal(identity);
                else
                    principal.AddIdentity(identity);
            }
        }

        return principal != null;
    }

    /// <summary>
    /// Validates the manifest signature and optional expiration.
    /// </summary>
    /// <param name="jwt">The JWT to validate.</param>
    /// <param name="jwk">The key to validate the manifest signature with.</param>
    /// <param name="token">Except when returning <see cref="Status.Unknown"/>, returns the security token read from the JWT, even if signature check failed.</param>
    /// <param name="identity">The associated claims, only when return value is not <see cref="Status.Unknown"/>.</param>
    /// <param name="requireExpiration">Whether to check for expiration.</param>
    /// <returns>The status of the validation.</returns>
    public static ManifestStatus Validate(string jwt, string jwk, out SecurityToken? token, out ClaimsIdentity? identity, bool validateExpiration)
    {
        token = default;
        identity = default;

        SecurityKey key;
        try
        {
            key = JsonWebKey.Create(jwk);
        }
        catch (ArgumentException)
        {
            return ManifestStatus.Unknown;
        }

        var handler = new JsonWebTokenHandler { MapInboundClaims = false };

        if (!handler.CanReadToken(jwt))
            return ManifestStatus.Unknown;

        var validation = new TokenValidationParameters
        {
            RequireExpirationTime = false,
            ValidateLifetime = false,
            ValidateAudience = false,
            ValidateIssuer = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            RoleClaimType = "roles",
            NameClaimType = "sub",
        };

        var result = handler.ValidateTokenAsync(jwt, validation).Result;
        if (result.Exception != null)
        {
            if (result.Exception is SecurityTokenInvalidSignatureException)
            {
                var jwtToken = handler.ReadJsonWebToken(jwt);
                token = jwtToken;
                identity = new ClaimsIdentity(jwtToken.Claims);
                return ManifestStatus.Invalid;
            }
            else
            {
                var jwtToken = handler.ReadJsonWebToken(jwt);
                token = jwtToken;
                identity = new ClaimsIdentity(jwtToken.Claims);
                return ManifestStatus.Invalid;
            }
        }

        token = result.SecurityToken;
        identity = new ClaimsIdentity(result.ClaimsIdentity.Claims, "JWT");

        if (validateExpiration && token.ValidTo == DateTime.MinValue)
            return ManifestStatus.Invalid;

        // The sponsorable manifest does not have an expiration time.
        if (validateExpiration && token.ValidTo < DateTimeOffset.UtcNow)
            return ManifestStatus.Expired;

        return ManifestStatus.Valid;
    }

    class JwtRolesPrincipal(ClaimsIdentity identity) : ClaimsPrincipal([identity])
    {
        public override bool IsInRole(string role) => HasClaim("roles", role) || base.IsInRole(role);
    }
}
