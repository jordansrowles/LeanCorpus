using Rowles.LeanCorpus.Server.Abstractions.Services;

if (!typeof(IHealthService).IsInterface || typeof(IHealthService).GetMethods().Length == 0)
    throw new InvalidDataException("The public health service contract is unavailable.");
Console.WriteLine("SERVER ABSTRACTIONS IHealthService");