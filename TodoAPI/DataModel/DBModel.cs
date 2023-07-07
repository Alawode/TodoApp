using System;
namespace TodoAPI.DataModel
{
    public abstract class DbModel
    {
        public Guid Id { get; set; }
    }

    public class User : DbModel
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
    }

    public class Todo : DbModel
    {
        public string Task { get; set; }
        public bool? Completed { get; set; }
        public Guid UserId { get; set; }
        public  bool IsDeleted { get; set; }

    }

}

