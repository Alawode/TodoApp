using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using TodoAPI.DataModel;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Microsoft.Identity.Client;
using System.Numerics;

namespace TodoAPI
{
    public static class TodoAPI
    {
        [FunctionName("TodoAPI")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            string name = req.Query["name"];

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            name = name ?? data?.name;

            string responseMessage = string.IsNullOrEmpty(name)
                ? "This HTTP triggered function executed successfully. Pass a name in the query string or in the request body for a personalized response."
                : $"Hello, {name}. This HTTP triggered function executed successfully.";

            return new OkObjectResult(responseMessage);
        }


        [FunctionName("CreateTodo")]
        public static async Task<IActionResult> CreateTodo(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
        ILogger log)
        {
            log.LogInformation("Processing a request to create a new Todo item.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            Todo data = JsonConvert.DeserializeObject<Todo>(requestBody);

            // basic input validation
            if (string.IsNullOrWhiteSpace(data.Task))
            {
                return new BadRequestObjectResult("Please provide a task description.");
            }
            if (data.UserId == Guid.Empty)
            {
                return new BadRequestObjectResult("Please provide a valid user ID.");
            }

            // Generate a new Guid for the todo item
            data.Id = Guid.NewGuid();

            var connectionString = Environment.GetEnvironmentVariable("SQLConnectionString");
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                var sqlQuery = "INSERT INTO Todo (Id, Task, Completed, UserId) VALUES (@Id, @Task, @Completed, @UserId)";

                using (SqlCommand cmd = new SqlCommand(sqlQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", Guid.NewGuid());
                    cmd.Parameters.AddWithValue("@Task", data.Task);
                    cmd.Parameters.AddWithValue("@Completed", data.Completed);
                    cmd.Parameters.AddWithValue("@UserId", data.UserId);

                    var rows = await cmd.ExecuteNonQueryAsync();

                    if (rows > 0)
                        return new OkObjectResult($"Todo item created successfully.");
                    else
                        return new BadRequestObjectResult("Error occurred while creating the Todo item.");
                }
            }
        }


        [FunctionName("UpdateTodo")]
        public static async Task<IActionResult> UpdateTodo(
        [HttpTrigger(AuthorizationLevel.Function, "put", Route = null)] HttpRequest req,
        ILogger log)
        {
            log.LogInformation("Processing a request to update a Todo item.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            Todo data = JsonConvert.DeserializeObject<Todo>(requestBody);

            if (data.Id == Guid.Empty)
            {
                return new BadRequestObjectResult("Please provide a valid Todo ID.");
            }

            var connectionString = Environment.GetEnvironmentVariable("SQLConnectionString");

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                var sqlQuery = new StringBuilder("UPDATE Todo SET ");

                if (data.Task != null)
                {
                    sqlQuery.Append("Task = @Task, ");
                }

                if (data.Completed.HasValue)
                {
                    sqlQuery.Append("Completed = @Completed, ");
                }

                // Remove the last comma and append the WHERE clause
                sqlQuery
                    .Remove(sqlQuery.Length - 2, 2)
                    .Append(" WHERE Id = @Id");

                using (SqlCommand cmd = new SqlCommand(sqlQuery.ToString(), conn))
                {
                    if (data.Task != null)
                    {
                        cmd.Parameters.AddWithValue("@Task", data.Task);
                    }

                    if (data.Completed.HasValue)
                    {
                        cmd.Parameters.AddWithValue("@Completed", data.Completed.Value);
                    }

                    cmd.Parameters.AddWithValue("@Id", data.Id);

                    var rows = await cmd.ExecuteNonQueryAsync();

                    if (rows > 0)
                        return new OkObjectResult($"Todo item updated successfully.");
                    else
                        return new BadRequestObjectResult("Error occurred while updating the Todo item.");
                }
            }
        }

        [FunctionName("DeleteTodo")]
        public static async Task<IActionResult> DeleteTodo(
        [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "todo/{id}")] HttpRequest req,
        ILogger log,
        string id)
        {
            log.LogInformation($"Processing a request to delete Todo item with ID {id}.");

            var connectionString = Environment.GetEnvironmentVariable("SQLConnectionString");

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                var sqlQuery = "UPDATE Todo SET IsDeleted = 1 WHERE Id = @Id";

                using (SqlCommand cmd = new SqlCommand(sqlQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);

                    var rows = await cmd.ExecuteNonQueryAsync();

                    if (rows > 0)
                        return new OkObjectResult($"Todo item with ID {id} deleted successfully.");
                    else
                        return new BadRequestObjectResult("Error occurred while deleting the Todo item or the item does not exist.");
                }
            }
        }

        [FunctionName("GetTodos")]
        public static async Task<IActionResult> GetTodos(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req,
        ILogger log)
        {
            log.LogInformation("Processing a request to fetch all non-deleted Todo items.");

            var connectionString = Environment.GetEnvironmentVariable("SQLConnectionString");
            var todoList = new List<Todo>();

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                var sqlQuery = "SELECT * FROM Todo WHERE IsDeleted = 0";

                using (SqlCommand cmd = new SqlCommand(sqlQuery, conn))
                {
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        while (reader.Read())
                        {
                            var todo = new Todo
                            {
                                Id = reader.GetGuid(reader.GetOrdinal("Id")),
                                Task = reader.GetString(reader.GetOrdinal("Task")),
                                Completed = reader.GetBoolean(reader.GetOrdinal("Completed")),
                                UserId = reader.GetGuid(reader.GetOrdinal("UserId")),
                                IsDeleted = reader.GetBoolean(reader.GetOrdinal("IsDeleted")),
                            };
                            todoList.Add(todo);
                        }
                    }
                }
            }

            if (todoList.Any())
                return new OkObjectResult(todoList);
            else
                return new NoContentResult();
        }

        [FunctionName("GetTodosByUser")]
        public static async Task<IActionResult> GetTodosByUser(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "todos/{userId}")] HttpRequest req,
        ILogger log, string userId)
        {
            log.LogInformation("Processing a request to fetch all non-deleted Todo items for a specific user.");

            var connectionString = Environment.GetEnvironmentVariable("SQLConnectionString");
            var todoList = new List<Todo>();
            Guid userGuid;

            // Check if provided userId is a valid Guid
            if (!Guid.TryParse(userId, out userGuid))
            {
                return new BadRequestObjectResult("Please provide a valid user ID.");
            }

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                var sqlQuery = "SELECT * FROM Todo WHERE UserId = @UserId AND IsDeleted = 0";

                using (SqlCommand cmd = new SqlCommand(sqlQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userGuid);

                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        while (reader.Read())
                        {
                            var todo = new Todo
                            {
                                Id = reader.GetGuid(reader.GetOrdinal("Id")),
                                Task = reader.GetString(reader.GetOrdinal("Task")),
                                Completed = reader.GetBoolean(reader.GetOrdinal("Completed")),
                                UserId = reader.GetGuid(reader.GetOrdinal("UserId")),
                                IsDeleted = reader.GetBoolean(reader.GetOrdinal("IsDeleted")),
                            };
                            todoList.Add(todo);
                        }
                    }
                }
            }

            if (todoList.Any())
                return new OkObjectResult(todoList);
            else
                return new NoContentResult();
        }

        [FunctionName("GetTodoById")]
        public static async Task<IActionResult> GetTodoById(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "todo/{id}")] HttpRequest req,
        ILogger log, string id)
        {
            log.LogInformation("Processing a request to fetch a Todo item by its ID.");

            var connectionString = Environment.GetEnvironmentVariable("SQLConnectionString");
            Guid todoId;

            // Check if provided id is a valid Guid
            if (!Guid.TryParse(id, out todoId))
            {
                return new BadRequestObjectResult("Please provide a valid ID.");
            }

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                var sqlQuery = "SELECT * FROM Todo WHERE Id = @Id AND IsDeleted = 0";

                using (SqlCommand cmd = new SqlCommand(sqlQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", todoId);

                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        if (reader.Read())
                        {
                            var todo = new Todo
                            {
                                Id = reader.GetGuid(reader.GetOrdinal("Id")),
                                Task = reader.GetString(reader.GetOrdinal("Task")),
                                Completed = reader.GetBoolean(reader.GetOrdinal("Completed")),
                                UserId = reader.GetGuid(reader.GetOrdinal("UserId")),
                                IsDeleted = reader.GetBoolean(reader.GetOrdinal("IsDeleted")),
                            };

                            return new OkObjectResult(todo);
                        }
                    }
                }
            }

            return new NotFoundResult();
        }

        [FunctionName("GetUserById")]
        public static async Task<IActionResult> GetUserById(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "user/{id}")] HttpRequest req,
        ILogger log, string id)
        {
            log.LogInformation("Processing a request to fetch a User by its ID.");

            var connectionString = Environment.GetEnvironmentVariable("SQLConnectionString");
            Guid userId;

            // Check if provided id is a valid Guid
            if (!Guid.TryParse(id, out userId))
            {
                return new BadRequestObjectResult("Please provide a valid ID.");
            }

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                var sqlQuery = "SELECT * FROM User WHERE Id = @Id";

                using (SqlCommand cmd = new SqlCommand(sqlQuery, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", userId);

                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        if (reader.Read())
                        {
                            var user = new User
                            {
                                Id = reader.GetGuid(reader.GetOrdinal("Id")),
                                FirstName = reader.GetString(reader.GetOrdinal("FirstName")),
                                LastName = reader.GetString(reader.GetOrdinal("LastName")),
                                Email = reader.GetString(reader.GetOrdinal("Email")),
                            };

                            return new OkObjectResult(user);
                        }
                    }
                }
            }

            return new NotFoundResult();
        }


        // This is a basic login implementation using msal. I would typically not do it this way
        //because it requires user credentials (username and password)
        //to be sent over the wire, which is not generally recommended.
        //A safer way to handle authentication would be to have the client-side application
        //redirect the user to a sign-in page provided by Microsoft (or another trusted Identity Provider like Auth0)
        // I would be happy to share that implementatin using both a client and server side apps :)
        //and then handle the returned authorization code server-side.
        [FunctionName("LoginUser")]
        public static async Task<IActionResult> LoginUser(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
        ILogger log)
        {
            log.LogInformation("Processing login request.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            User data = JsonConvert.DeserializeObject<User>(requestBody);

            if (string.IsNullOrWhiteSpace(data.Email) || string.IsNullOrWhiteSpace(data.Password))
            {
                return new BadRequestObjectResult("Please provide both Email and Password.");
            }

            var clientApp = PublicClientApplicationBuilder.Create("<Your-App-ID>")
                .WithTenantId("<Your-Tenant-ID>")
                .WithDefaultRedirectUri()
                .Build();

            var scopes = new[] { "<Your-API-Scopes>" };

            try
            {
                var result = await clientApp.AcquireTokenByUsernamePassword(scopes, data.Email, data.Password).ExecuteAsync();
                return new OkObjectResult(new { token = result.AccessToken });
            }
            catch (MsalException ex)
            {
                log.LogError($"Error acquiring token: {ex.Message}");
                return new BadRequestObjectResult("Error during login, please check your credentials.");
            }
        }

    }
}

