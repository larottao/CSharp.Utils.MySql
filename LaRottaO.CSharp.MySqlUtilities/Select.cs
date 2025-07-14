using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

public static class MySqlUtilities
{
    ///
    /// Executes a SELECT query asynchronously, maps the results to a list of objects, and supports cancellation.
    ///
    /// Eexample
    ///
    /// // 1. Define your data model
    /// public class Product
    /// {
    ///     public int ProductId { get; set; }
    ///     public string ProductName { get; set; }
    /// }
    ///
    /// // 2. Call the method
    /// public async Task GetProducts()
    /// {
    ///     var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)); // Cancel after 10s
    ///
    ///     var (success, error, products) = await MySqlUtilities.SelectAsync(
    ///         connectionString: "server=your_server;database=your_db;user=your_user;password=your_pass;",
    ///         query: "SELECT ProductId, ProductName FROM Products WHERE CategoryId = @Id;",
    ///         parameters: new Dictionary&lt;string, object&gt; { { "@Id", 5 } },
    ///         cancellationToken: cts.Token
    ///     );
    ///
    ///     if (success)
    ///     {
    ///         Console.WriteLine($"Found {products.Count} products.");
    ///     }
    ///     else
    ///     {
    ///         Console.WriteLine($"Error: {error}");
    ///     }
    /// }
    ///
    ///
    ///

    public static async Task<(bool success, string errorMessage, List<T> data)> SelectAsync<T>(
        string connectionString,
        string query,
        Dictionary<string, object> parameters = null,
        int commandTimeoutSeconds = 30,
        CancellationToken cancellationToken = default) where T : new()
    {
        var outputList = new List<T>();

        try
        {
            using (var connection = new MySqlConnection(connectionString))
            {
                await connection.OpenAsync(cancellationToken);

                using (var cmd = new MySqlCommand(query, connection))
                {
                    cmd.CommandTimeout = commandTimeoutSeconds;

                    if (parameters != null)
                    {
                        foreach (var param in parameters)
                        {
                            cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                        }
                    }

                    using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                    {
                        var columnMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            columnMap.Add(reader.GetName(i), i);
                        }

                        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

                        while (await reader.ReadAsync(cancellationToken))
                        {
                            T destinationObject = new T();

                            foreach (var property in properties)
                            {
                                if (columnMap.TryGetValue(property.Name, out int columnIndex))
                                {
                                    var dbValue = reader.GetValue(columnIndex);

                                    if (dbValue != DBNull.Value)
                                    {
                                        try
                                        {
                                            var propType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                                            var safeValue = Convert.ChangeType(dbValue, propType);
                                            property.SetValue(destinationObject, safeValue);
                                        }
                                        catch (Exception ex)
                                        {
                                            Debug.WriteLine($"Could not convert property '{property.Name}'. Error: {ex.Message}");
                                        }
                                    }
                                }
                            }
                            outputList.Add(destinationObject);
                        }
                    }
                }
            }
            return (true, null, outputList);
        }
        catch (OperationCanceledException)
        {
            return (false, "Query was canceled.", new List<T>());
        }
        catch (MySqlException sqlEx)
        {
            return (false, $"MySQL Error {sqlEx.Number}: {sqlEx.Message}", new List<T>());
        }
        catch (Exception ex)
        {
            return (false, $"An unexpected error occurred: {ex.Message}", new List<T>());
        }
    }
}