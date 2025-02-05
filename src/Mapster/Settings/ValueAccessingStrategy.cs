﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Mapster.Models;
using Mapster.Utils;
using ValueAccess = System.Func<System.Linq.Expressions.Expression, Mapster.Models.IMemberModel, Mapster.CompileArgument, System.Linq.Expressions.Expression?>;

namespace Mapster
{
    public static class ValueAccessingStrategy
    {
        public static readonly ValueAccess CustomResolver = CustomResolverFn;
        public static readonly ValueAccess PropertyOrField = PropertyOrFieldFn;
        public static readonly ValueAccess GetMethod = GetMethodFn;
        public static readonly ValueAccess FlattenMember = FlattenMemberFn;
        public static readonly ValueAccess Dictionary = DictionaryFn;
        public static readonly ValueAccess CustomResolverForDictionary = CustomResolverForDictionaryFn;

        public static readonly HashSet<ValueAccess> CustomResolvers = new HashSet<ValueAccess>
        {
            CustomResolver,
            CustomResolverForDictionary,
        };

        private static Expression? CustomResolverFn(Expression source, IMemberModel destinationMember, CompileArgument arg)
        {
            var config = arg.Settings;
            var resolvers = config.Resolvers;
            if (resolvers == null || resolvers.Count <= 0)
                return null;

            var invokes = new List<Tuple<Expression, Expression>>();

            Expression? getter = null;
            foreach (var resolver in resolvers)
            {
                if (!destinationMember.Name.Equals(resolver.DestinationMemberName))
                    continue;
                var invoke = resolver.Invoker == null
                    ? ExpressionEx.PropertyOrField(source, resolver.SourceMemberName)
                    : resolver.IsChildPath
                    ? resolver.Invoker.Body
                    : resolver.Invoker.Apply(arg.MapType, source);

                if (resolver.Condition == null)
                {
                    getter = invoke;
                    break;
                }

                var condition = resolver.IsChildPath
                    ? resolver.Condition.Body
                    : resolver.Condition.Apply(arg.MapType, source);
                invokes.Add(Tuple.Create(condition, invoke));
            }

            if (invokes.Count > 0)
            {
                invokes.Reverse();
                if (getter == null)
                {
                    var type = invokes[0].Item2.Type;
                    getter = type.CreateDefault();
                }
                foreach (var invoke in invokes)
                {
                    getter = Expression.Condition(invoke.Item1, invoke.Item2, getter);
                }
            }

            return getter;
        }

        private static Expression? PropertyOrFieldFn(Expression source, IMemberModel destinationMember, CompileArgument arg)
        {
            var members = source.Type.GetFieldsAndProperties(accessorFlags: BindingFlags.NonPublic | BindingFlags.Public);
            var strategy = arg.Settings.NameMatchingStrategy;
            var destinationMemberName = destinationMember.GetMemberName(arg.Settings.GetMemberNames, strategy.DestinationMemberNameConverter);
            return members
                .Where(member => member.ShouldMapMember(arg, MemberSide.Source))
                .Where(member => member.GetMemberName(arg.Settings.GetMemberNames, strategy.SourceMemberNameConverter) == destinationMemberName)
                .Select(member => member.GetExpression(source))
                .FirstOrDefault();
        }

        private static Expression? GetMethodFn(Expression source, IMemberModel destinationMember, CompileArgument arg)
        {
            if (arg.MapType == MapType.Projection)
                return null;
            var strategy = arg.Settings.NameMatchingStrategy;
            var destinationMemberName = "Get" + destinationMember.GetMemberName(arg.Settings.GetMemberNames, strategy.DestinationMemberNameConverter);
            var getMethod = source.Type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => strategy.SourceMemberNameConverter(m.Name) == destinationMemberName && m.GetParameters().Length == 0);
            if (getMethod == null)
                return null;
            if (getMethod.Name == "GetType" && destinationMember.Type != typeof(Type))
                return null;
            return Expression.Call(source, getMethod);
        }

        private static Expression? FlattenMemberFn(Expression source, IMemberModel destinationMember, CompileArgument arg)
        {
            var strategy = arg.Settings.NameMatchingStrategy;
            var destinationMemberName = destinationMember.GetMemberName(arg.Settings.GetMemberNames, strategy.DestinationMemberNameConverter);
            return GetDeepFlattening(source, destinationMemberName, arg);
        }

        private static Expression? GetDeepFlattening(Expression source, string propertyName, CompileArgument arg)
        {
            var strategy = arg.Settings.NameMatchingStrategy;
            var members = source.Type.GetFieldsAndProperties(accessorFlags: BindingFlags.NonPublic | BindingFlags.Public);
            foreach (var member in members)
            {
                if (!member.ShouldMapMember(arg, MemberSide.Source))
                    continue;
                var sourceMemberName = member.GetMemberName(arg.Settings.GetMemberNames, strategy.SourceMemberNameConverter);
                var propertyType = member.Type;
                if (propertyName.StartsWith(sourceMemberName) &&
                    (propertyType.IsPoco() || propertyType.IsRecordType()))
                {
                    var exp = member.GetExpression(source);
                    var ifTrue = GetDeepFlattening(exp, propertyName.Substring(sourceMemberName.Length).TrimStart('_'), arg);
                    if (ifTrue == null)
                        continue;
                    return arg.MapType == MapType.Projection ? ifTrue : exp.NullPropagate(ifTrue);
                }

                if (string.Equals(propertyName, sourceMemberName))
                    return member.GetExpression(source);
            }
            return null;
        }

        internal static IEnumerable<InvokerModel> FindUnflatteningPairs(Expression source, IMemberModel destinationMember, CompileArgument arg)
        {
            var strategy = arg.Settings.NameMatchingStrategy;
            var destinationMemberName = destinationMember.GetMemberName(arg.Settings.GetMemberNames, strategy.DestinationMemberNameConverter);
            var members = source.Type.GetFieldsAndProperties(accessorFlags: BindingFlags.NonPublic | BindingFlags.Public);

            foreach (var member in members)
            {
                if (!member.ShouldMapMember(arg, MemberSide.Source))
                    continue;
                var sourceMemberName = member.GetMemberName(arg.Settings.GetMemberNames, strategy.SourceMemberNameConverter);
                if (!sourceMemberName.StartsWith(destinationMemberName) || sourceMemberName == destinationMemberName)
                    continue;
                foreach (var prop in GetDeepUnflattening(destinationMember, sourceMemberName.Substring(destinationMemberName.Length).TrimStart('_'), arg))
                {
                    yield return new InvokerModel
                    {
                        SourceMemberName = member.Name,
                        DestinationMemberName = destinationMember.Name + "." + prop,
                    };
                }
            }
        }

        private static IEnumerable<string> GetDeepUnflattening(IMemberModel destinationMember, string propertyName, CompileArgument arg)
        {
            var strategy = arg.Settings.NameMatchingStrategy;
            var members = destinationMember.Type.GetFieldsAndProperties(accessorFlags: BindingFlags.NonPublic | BindingFlags.Public);
            foreach (var member in members)
            {
                if (!member.ShouldMapMember(arg, MemberSide.Destination))
                    continue;
                var destMemberName = member.GetMemberName(arg.Settings.GetMemberNames, strategy.DestinationMemberNameConverter);
                var propertyType = member.Type;
                if (propertyName.StartsWith(destMemberName) &&
                    (propertyType.IsPoco() || propertyType.IsRecordType()))
                {
                    foreach (var prop in GetDeepUnflattening(member, propertyName.Substring(destMemberName.Length).TrimStart('_'), arg))
                    {
                        yield return member.Name + "." + prop;
                    }
                }
                else if (string.Equals(propertyName, destMemberName))
                {
                    yield return member.Name;
                }
            }
        }

        private static Expression? DictionaryFn(Expression source, IMemberModel destinationMember, CompileArgument arg)
        {
            var dictType = source.Type.GetDictionaryType();
            if (dictType == null)
                return null;

            var strategy = arg.Settings.NameMatchingStrategy;
            var destinationMemberName = destinationMember.GetMemberName(arg.Settings.GetMemberNames, strategy.DestinationMemberNameConverter);
            var key = Expression.Constant(destinationMemberName);
            var args = dictType.GetGenericArguments();
            if (strategy.SourceMemberNameConverter != NameMatchingStrategy.Identity)
            {
                var method = typeof(MapsterHelper).GetMethods().First(m => m.Name == nameof(MapsterHelper.FlexibleGet)).MakeGenericMethod(args[1]);
                return Expression.Call(method, source.To(dictType), key, MapsterHelper.GetConverterExpression(strategy.SourceMemberNameConverter));
            }
            else
            {
                var method = typeof(MapsterHelper).GetMethods().First(m => m.Name == nameof(MapsterHelper.GetValueOrDefault)).MakeGenericMethod(args);
                return Expression.Call(method, source.To(dictType), key);
            }
        }

        private static Expression? CustomResolverForDictionaryFn(Expression source, IMemberModel destinationMember, CompileArgument arg)
        {
            var config = arg.Settings;
            var resolvers = config.Resolvers;
            if (resolvers == null || resolvers.Count <= 0)
                return null;
            var dictType = source.Type.GetDictionaryType();
            if (dictType == null)
                return null;
            var args = dictType.GetGenericArguments();
            var method = typeof(MapsterHelper).GetMethods().First(m => m.Name == nameof(MapsterHelper.GetValueOrDefault)).MakeGenericMethod(args);

            Expression? getter = null;
            LambdaExpression? lastCondition = null;
            foreach (var resolver in resolvers)
            {
                if (!destinationMember.Name.Equals(resolver.DestinationMemberName))
                    continue;

                Expression invoke = resolver.Invoker == null
                    ? Expression.Call(method, source.To(dictType), Expression.Constant(resolver.SourceMemberName))
                    : resolver.Invoker.Apply(arg.MapType, source);
                getter = lastCondition != null
                    ? Expression.Condition(lastCondition.Apply(arg.MapType, source), getter, invoke)
                    : invoke;
                lastCondition = resolver.Condition;
                if (resolver.Condition == null)
                    break;
            }
            if (lastCondition != null)
                getter = Expression.Condition(lastCondition.Apply(arg.MapType, source), getter!, getter!.Type.CreateDefault());
            return getter;
        }
    }
}
