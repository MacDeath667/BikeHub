namespace BikeHub.Dal;

public record Bike
{
    public Guid Id { get; }
    public Guid OwnerId { get; }
    public string Name { get; }
    public string Description { get; }

    public Bike()
    {
    }

    public Bike(Guid Id, Guid OwnerId, string Name, string Description) : this()
    {
        this.Id = Id;
        this.OwnerId = OwnerId;
        this.Name = Name;
        this.Description = Description;
    }
}