namespace BikeHub.Domain;

public class Bike(Guid ownerId, string name)
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public string Name { get; private set; } = name;

    public Guid OwnerId { get; private set; } = ownerId;
}