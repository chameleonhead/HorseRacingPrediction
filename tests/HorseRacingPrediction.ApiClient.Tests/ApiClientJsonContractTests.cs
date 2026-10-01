using HorseRacingPrediction.ApiClient;
using HorseRacingPrediction.ApiClient.Collection;
using HorseRacingPrediction.ApiClient.Horses;
using HorseRacingPrediction.ApiClient.Identity;
using HorseRacingPrediction.ApiClient.Jockeys;
using HorseRacingPrediction.ApiClient.MachineLearning;
using HorseRacingPrediction.ApiClient.Memos;
using HorseRacingPrediction.ApiClient.Owners;
using HorseRacingPrediction.ApiClient.Predictions;
using HorseRacingPrediction.ApiClient.PredictionScheduling;
using HorseRacingPrediction.ApiClient.Races;
using HorseRacingPrediction.ApiClient.Repairs;
using HorseRacingPrediction.ApiClient.Subjects;
using HorseRacingPrediction.ApiClient.Trainers;
using Refit;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace HorseRacingPrediction.ApiClient.Tests;

[TestClass]
public sealed class ApiClientJsonContractTests
{
    private static readonly Type[] ApiInterfaces =
    [
        typeof(IRacesApi), typeof(IHorsesApi), typeof(IJockeysApi), typeof(ITrainersApi),
        typeof(IOwnersApi), typeof(IPredictionsApi), typeof(IMemosApi), typeof(IMachineLearningApi),
        typeof(IPredictionSchedulingApi), typeof(IRepairsApi), typeof(IIdentityApi),
        typeof(ISubjectsApi), typeof(ICollectionApi)
    ];

    [TestMethod]
    public void PublicSurface_Has135OperationMethodsWithTypedRequestAndResponseShapes()
    {
        var expected = new Dictionary<Type, (int Operations, int Requests, int Responses)>
        {
            [typeof(IRacesApi)] = (29, 29, 17),
            [typeof(IHorsesApi)] = (9, 9, 6),
            [typeof(IJockeysApi)] = (8, 8, 5),
            [typeof(ITrainersApi)] = (7, 7, 4),
            [typeof(IOwnersApi)] = (6, 5, 4),
            [typeof(IPredictionsApi)] = (11, 11, 3),
            [typeof(IMemosApi)] = (5, 5, 2),
            [typeof(IMachineLearningApi)] = (2, 1, 2),
            [typeof(IPredictionSchedulingApi)] = (3, 3, 1),
            [typeof(IRepairsApi)] = (7, 5, 7),
            [typeof(IIdentityApi)] = (2, 2, 2),
            [typeof(ISubjectsApi)] = (2, 2, 1),
            [typeof(ICollectionApi)] = (44, 30, 40)
        };
        foreach (var api in ApiInterfaces)
        {
            var familyMethods = api.GetMethods();
            Assert.AreEqual(expected[api].Operations, familyMethods.Length, api.Name);
            Assert.AreEqual(expected[api].Requests, familyMethods.Count(method => method.GetParameters().Length == 2), api.Name);
            Assert.AreEqual(expected[api].Responses, familyMethods.Count(method =>
                method.ReturnType.GenericTypeArguments[0].IsGenericType
                && method.ReturnType.GenericTypeArguments[0].GetGenericTypeDefinition() == typeof(ApiResponse<>)), api.Name);
        }

        var methods = ApiInterfaces.SelectMany(api => api.GetMethods()).ToArray();
        Assert.HasCount(135, methods);

        var dataOperations = 0;
        var noDataOperations = 0;
        var requests = new HashSet<Type>();
        var responses = new HashSet<Type>();
        foreach (var method in methods)
        {
            Assert.AreEqual(typeof(Task<>), method.ReturnType.GetGenericTypeDefinition(), method.Name);
            var returnType = method.ReturnType.GenericTypeArguments[0];
            var parameters = method.GetParameters();
            Assert.IsTrue(parameters.Length is 1 or 2, method.Name);
            Assert.AreEqual(typeof(CancellationToken), parameters[^1].ParameterType, method.Name);
            Assert.AreEqual("cancellationToken", parameters[^1].Name, method.Name);

            if (parameters.Length == 2)
            {
                var requestType = parameters[0].ParameterType;
                StringAssert.EndsWith(requestType.Name, "Request", method.Name);
                requests.Add(requestType);
            }

            if (returnType == typeof(IApiResponse))
            {
                noDataOperations++;
                continue;
            }

            Assert.AreEqual(typeof(ApiResponse<>), returnType.GetGenericTypeDefinition(), method.Name);
            dataOperations++;
            var responseType = returnType.GenericTypeArguments[0];
            StringAssert.EndsWith(responseType.Name, "Response", method.Name);
            responses.Add(responseType);
        }

        Assert.AreEqual(94, dataOperations);
        Assert.AreEqual(41, noDataOperations);
        Assert.AreEqual(117, methods.Sum(method => method.GetParameters().Count(parameter =>
            parameter.ParameterType != typeof(CancellationToken))));
        Assert.AreEqual(117, requests.Count);
        Assert.AreEqual(94, responses.Count);
    }

    [TestMethod]
    public void EveryPublicRequestAndResponseRootHasGeneratedMetadataForItsReachableGraph()
    {
        var options = ApiClientJsonContext.CreateOptions();
        var resolver = options.TypeInfoResolver;
        Assert.IsNotNull(resolver);
        Assert.IsFalse(resolver is DefaultJsonTypeInfoResolver,
            "The client JSON options must use generated metadata rather than reflection fallback.");
        Assert.IsNull(resolver.GetTypeInfo(typeof(ApiClientTestUnregisteredWireType), options),
            "Unregistered types must not silently use reflection serialization metadata.");

        var roots = new HashSet<Type>();
        foreach (var method in ApiInterfaces.SelectMany(api => api.GetMethods()))
        {
            var request = method.GetParameters()
                .FirstOrDefault(parameter => parameter.ParameterType != typeof(CancellationToken));
            if (request is not null)
                roots.Add(request.ParameterType);

            var response = method.ReturnType.GenericTypeArguments[0];
            if (response.IsGenericType && response.GetGenericTypeDefinition() == typeof(ApiResponse<>))
                roots.Add(response.GenericTypeArguments[0]);
        }

        var toVisit = new Queue<Type>(roots);
        var visited = new HashSet<Type>();
        while (toVisit.TryDequeue(out var type))
        {
            if (type == typeof(string) || type.IsPrimitive || type.IsEnum || type == typeof(decimal)
                || type == typeof(DateOnly) || type == typeof(DateTime) || type == typeof(DateTimeOffset)
                || type == typeof(TimeOnly) || type == typeof(TimeSpan) || type == typeof(Guid))
            {
                if (!type.IsEnum)
                    continue;
            }

            if (!visited.Add(type))
                continue;

            var typeInfo = resolver.GetTypeInfo(type, options);
            Assert.IsNotNull(typeInfo, $"No generated JSON metadata for {type.FullName}.");

            if (typeInfo.Kind == JsonTypeInfoKind.Object)
            {
                foreach (var property in typeInfo.Properties)
                    EnqueueWireTypes(property.PropertyType, toVisit);
            }
            else
            {
                EnqueueWireTypes(type, toVisit);
            }
        }
    }

    [TestMethod]
    public void PublicMethods_MatchEveryFrozenOperationNameAndContractRoot()
    {
        var root = FindRepositoryRoot();
        using var map = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs", "changes",
            "20260930_refit-api-clients", "t4-api-method-map.json")));
        var expectedCount = 0;

        foreach (var api in map.RootElement.GetProperty("interfaces").EnumerateArray())
        {
            var apiType = ApiInterfaces.Single(type =>
                type.Name == api.GetProperty("name").GetString()
                && type.Namespace == api.GetProperty("namespace").GetString());
            var methods = api.GetProperty("operations").EnumerateArray().ToArray();
            Assert.AreEqual(methods.Length, apiType.GetMethods().Length, apiType.Name);

            foreach (var operation in methods)
            {
                expectedCount++;
                var methodName = operation.GetProperty("method").GetString()!;
                var method = apiType.GetMethod(methodName);
                Assert.IsNotNull(method, $"{apiType.Name}.{methodName} is missing.");
                var requestPresent = operation.GetProperty("requestPresent").GetBoolean();
                var expectedParameters = requestPresent ? 2 : 1;
                Assert.AreEqual(expectedParameters, method.GetParameters().Length, methodName);
                if (requestPresent)
                    Assert.AreEqual(operation.GetProperty("requestContract").GetString(),
                        method.GetParameters()[0].ParameterType.Name, methodName);
                Assert.AreEqual(typeof(CancellationToken), method.GetParameters()[^1].ParameterType, methodName);

                var expectedResponse = operation.GetProperty("responseContracts").EnumerateArray()
                    .Where(contract => contract.TryGetProperty("type", out var type)
                        && type.ValueKind == JsonValueKind.String
                        && contract.GetProperty("status").GetInt32() < 400)
                    .Select(contract => contract.GetProperty("type").GetString()!.Split('.').Last())
                    .FirstOrDefault();
                var actualResponse = method.ReturnType.GenericTypeArguments[0];
                if (expectedResponse is null)
                    Assert.AreEqual(typeof(IApiResponse), actualResponse, methodName);
                else
                {
                    Assert.AreEqual(typeof(ApiResponse<>), actualResponse.GetGenericTypeDefinition(), methodName);
                    Assert.AreEqual(expectedResponse, actualResponse.GenericTypeArguments[0].Name, methodName);
                }
            }
        }

        Assert.AreEqual(135, expectedCount);
    }

    private static void EnqueueWireTypes(Type type, Queue<Type> queue)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsArray)
        {
            queue.Enqueue(type.GetElementType()!);
            return;
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(Dictionary<,>) || definition == typeof(IReadOnlyDictionary<,>)
                || definition == typeof(IDictionary<,>))
            {
                queue.Enqueue(type.GenericTypeArguments[0]);
                queue.Enqueue(type.GenericTypeArguments[1]);
                return;
            }
            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type)
                || definition == typeof(IEnumerable<>) || definition == typeof(IReadOnlyList<>))
            {
                foreach (var argument in type.GenericTypeArguments)
                    queue.Enqueue(argument);
                return;
            }
        }

        queue.Enqueue(type);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "HorseRacingPrediction.sln")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Could not find the repository root for the frozen T4 operation map.");
    }

}
