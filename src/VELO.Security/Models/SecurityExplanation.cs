namespace VELO.Security.Models;

public record SecurityExplanation(
    string WhatHappened,
    string WhyBlocked,
    string WhatItMeans,
    string? LearnMoreUrl = null);
