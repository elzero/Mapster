﻿using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace Mapster.Tests
{
    [TestClass]
    public class WhenMappingWithPath
    {
        [TestInitialize]
        public void Setup()
        {
            TypeAdapterConfig.GlobalSettings.Clear();
        }

        [TestMethod]
        public void MapPath()
        {
            TypeAdapterConfig<Poco, Dto>.NewConfig()
                .Map(dest => dest.Address.Number, src => src.Child.Address.Number);
            TypeAdapterConfig<ChildPoco, Dto>.NewConfig()
                .Map(dest => dest.Address.Number, src => src.Address.Number + "test");

            var poco = new Poco
            {
                Id = Guid.NewGuid(),
                Child = new ChildPoco
                {
                    Address = new Address
                    {
                        Number = "123",
                        Location = "10,10"
                    }
                }
            };
            var dto = poco.Adapt<Dto>();
            dto.Id.ShouldBe(poco.Id);
            dto.Address.Number.ShouldBe(poco.Child.Address.Number);
            dto.Address.Location.ShouldBeNull();

            var dto2 = poco.Child.Adapt<Dto>();
            dto2.Address.Number.ShouldBe(poco.Child.Address.Number + "test");
        }

        [TestMethod]
        public void MapPath_Condition()
        {
            TypeAdapterConfig<Poco, Dto>.NewConfig()
                .Map(dest => dest.Address.Number, src => src.Child.Address.Number + "1", src => src.Child.Address.Location != null)
                .Map(dest => dest.Address.Number, src => src.Child.Address.Number + "2");
            TypeAdapterConfig<ChildPoco, Dto>.NewConfig()
                .Map(dest => dest.Address.Number, src => src.Address.Number + "1", src => src.Address.Location != null)
                .Map(dest => dest.Address.Number, src => src.Address.Number + "2");

            var poco = new Poco
            {
                Id = Guid.NewGuid(),
                Child = new ChildPoco
                {
                    Address = new Address
                    {
                        Number = "123",
                        Location = "10,10"
                    }
                }
            };

            var dto = poco.Adapt<Dto>();
            dto.Id.ShouldBe(poco.Id);
            dto.Address.Number.ShouldBe(poco.Child.Address.Number + "1");
            dto.Address.Location.ShouldBeNull();

            var dto2 = poco.Child.Adapt<Dto>();
            dto2.Address.Number.ShouldBe(poco.Child.Address.Number + "1");

            poco.Child.Address.Location = null;
            var dto3 = poco.Adapt<Dto>();
            dto3.Address.Number.ShouldBe(poco.Child.Address.Number + "2");

            var dto4 = poco.Child.Adapt<Dto>();
            dto4.Address.Number.ShouldBe(poco.Child.Address.Number + "2");
        }

        [TestMethod]
        public void IgnorePath()
        {
            TypeAdapterConfig<Poco, Dto>.NewConfig()
                .Map(dest => dest.Address, src => src.Child.Address)
                .Ignore(dest => dest.Address.Location);

            var poco = new Poco
            {
                Id = Guid.NewGuid(),
                Child = new ChildPoco
                {
                    Address = new Address
                    {
                        Number = "123",
                        Location = "10,10"
                    }
                }
            };
            var dto = poco.Adapt<Dto>();
            dto.Id.ShouldBe(poco.Id);
            dto.Address.Number.ShouldBe(poco.Child.Address.Number);
            dto.Address.Location.ShouldBeNull();
        }

        [TestMethod]
        public void IgnoreIfPath()
        {
            TypeAdapterConfig<Poco, Dto>.NewConfig()
                .Map(dest => dest.Address, src => src.Child.Address)
                .IgnoreIf((src, dest) => src.Child.Address.Number == "100", dest => dest.Address.Location);

            var poco = new Poco
            {
                Id = Guid.NewGuid(),
                Child = new ChildPoco
                {
                    Address = new Address
                    {
                        Number = "123",
                        Location = "10,10"
                    }
                }
            };

            var dto = poco.Adapt<Dto>();
            dto.Id.ShouldBe(poco.Id);
            dto.Address.Number.ShouldBe(poco.Child.Address.Number);
            dto.Address.Location.ShouldBe(poco.Child.Address.Location);

            poco.Child.Address.Number = "100";
            var dto2 = poco.Adapt<Dto>();
            dto2.Id.ShouldBe(poco.Id);
            dto2.Address.Number.ShouldBe(poco.Child.Address.Number);
            dto2.Address.Location.ShouldBeNull();
        }

        public class Poco
        {
            public Guid Id { get; set; }
            public ChildPoco Child { get; set; }
        }
        public class ChildPoco
        {
            public Address Address { get; set; }
        }
        public class Address
        {
            public string Number { get; set; }
            public string Location { get; set; }
        }
        public class Dto
        {
            public Guid Id { get; set; }
            public Address Address { get; set; }
        }
    }
}
