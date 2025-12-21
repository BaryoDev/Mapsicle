using System;
using Xunit;
using Mapsicle.Fluent;

namespace Mapsicle.Fluent.Tests
{
    public class Phase3AutoMapperGapsTests
    {
        #region Test Models

        // Base types for polymorphism
        public class Vehicle
        {
            public int Id { get; set; }
            public string Make { get; set; } = "";
        }

        public class Car : Vehicle
        {
            public int Doors { get; set; }
        }

        public class Truck : Vehicle
        {
            public int LoadCapacity { get; set; }
        }

        public class VehicleDto
        {
            public int Id { get; set; }
            public string Make { get; set; } = "";
        }

        public class CarDto : VehicleDto
        {
            public int Doors { get; set; }
        }

        public class TruckDto : VehicleDto
        {
            public int LoadCapacity { get; set; }
        }

        // For ConstructUsing
        public class Order
        {
            public int Id { get; set; }
            public string Type { get; set; } = "";
        }

        public class OrderDto
        {
            public int Id { get; set; }
            public string Type { get; set; } = "";
            public DateTime CreatedAt { get; set; }

            public OrderDto() { }
            public OrderDto(DateTime createdAt) => CreatedAt = createdAt;
        }

        // For Converters
        public class Money
        {
            public decimal Amount { get; set; }
            public string Currency { get; set; } = "USD";
        }

        #endregion

        #region Include Tests (Polymorphic)

        [Fact]
        public void Include_ShouldMapDerivedTypes()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Vehicle, VehicleDto>()
                    .Include<Car, CarDto>()
                    .Include<Truck, TruckDto>();
                cfg.CreateMap<Car, CarDto>();
                cfg.CreateMap<Truck, TruckDto>();
            });

            var mapper = config.CreateMapper();
            
            // Map Car as Vehicle
            Vehicle car = new Car { Id = 1, Make = "Toyota", Doors = 4 };
            var carDto = mapper.Map<CarDto>(car);

            Assert.NotNull(carDto);
            Assert.Equal(1, carDto.Id);
            Assert.Equal("Toyota", carDto.Make);
            Assert.Equal(4, carDto.Doors);
        }

        #endregion

        #region ConstructUsing Tests

        [Fact]
        public void ConstructUsing_ShouldUseFactory()
        {
            var now = new DateTime(2024, 1, 1);

            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateMap<Order, OrderDto>()
                    .ConstructUsing(src => new OrderDto(now));
            });

            var mapper = config.CreateMapper();
            var order = new Order { Id = 1, Type = "Standard" };

            var dto = mapper.Map<OrderDto>(order);

            Assert.NotNull(dto);
            Assert.Equal(now, dto.CreatedAt);
        }

        #endregion

        #region Global Converter Tests

        [Fact]
        public void CreateConverter_ShouldApplyGlobally()
        {
            var config = new MapperConfiguration(cfg =>
            {
                cfg.CreateConverter<Money, decimal>(m => m.Amount);
                cfg.CreateConverter<Money, string>(m => $"{m.Currency} {m.Amount}");
            });

            var mapper = config.CreateMapper();
            var money = new Money { Amount = 99.99m, Currency = "USD" };

            var amount = mapper.Map<decimal>(money);
            var display = mapper.Map<string>(money);

            Assert.Equal(99.99m, amount);
            Assert.Equal("USD 99.99", display);
        }

        #endregion
    }
}
