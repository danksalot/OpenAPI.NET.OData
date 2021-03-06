﻿// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// ------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.OData.Edm;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.OData.Common;
using Microsoft.OpenApi.OData.Capabilities;
using Microsoft.OData.Edm.Vocabularies;
using Microsoft.OpenApi.OData.Edm;

namespace Microsoft.OpenApi.OData.Generator
{
    /// <summary>
    /// Extension methods to create <see cref="OpenApiParameter"/> by Edm model.
    /// </summary>
    internal static class OpenApiParameterGenerator
    {
        /// <summary>
        /// 4.6.2 Field parameters in components
        /// Create a map of <see cref="OpenApiParameter"/> object.
        /// </summary>
        /// <param name="context">The OData context.</param>
        /// <returns>The created map of <see cref="OpenApiParameter"/> object.</returns>
        public static IDictionary<string, OpenApiParameter> CreateParameters(this ODataContext context)
        {
            Utils.CheckArgumentNull(context, nameof(context));
            
            // It allows defining query options and headers that can be reused across operations of the service.
            // The value of parameters is a map of Parameter Objects.
            return new Dictionary<string, OpenApiParameter>
            {
                { "top", CreateTop(context.Settings.TopExample) },
                { "skip", CreateSkip() },
                { "count", CreateCount() },
                { "filter", CreateFilter() },
                { "search", CreateSearch() },
            };
        }

        /// <summary>
        /// Create the list of <see cref="OpenApiParameter"/> for a <see cref="IEdmFunctionImport"/>.
        /// </summary>
        /// <param name="context">The OData context.</param>
        /// <param name="functionImport">The Edm function import.</param>
        /// <returns>The created list of <see cref="OpenApiParameter"/>.</returns>
        public static IList<OpenApiParameter> CreateParameters(this ODataContext context, IEdmFunctionImport functionImport)
        {
            Utils.CheckArgumentNull(context, nameof(context));
            Utils.CheckArgumentNull(functionImport, nameof(functionImport));

            return context.CreateParameters(functionImport.Function);
        }

        /// <summary>
        /// Create the list of <see cref="OpenApiParameter"/> for a <see cref="IEdmFunction"/>.
        /// </summary>
        /// <param name="context">The OData context.</param>
        /// <param name="function">The Edm function.</param>
        /// <returns>The created list of <see cref="OpenApiParameter"/>.</returns>
        public static IList<OpenApiParameter> CreateParameters(this ODataContext context, IEdmFunction function)
        {
            Utils.CheckArgumentNull(context, nameof(context));
            Utils.CheckArgumentNull(function, nameof(function));

            IList<OpenApiParameter> parameters = new List<OpenApiParameter>();
            int skip = function.IsBound ? 1 : 0;
            foreach (IEdmOperationParameter edmParameter in function.Parameters.Skip(skip))
            {
                // Structured or collection-valued parameters are represented as a parameter alias
                // in the path template and the parameters array contains a Parameter Object for
                // the parameter alias as a query option of type string.
                if (edmParameter.Type.IsStructured() ||
                    edmParameter.Type.IsCollection())
                {
                    parameters.Add(new OpenApiParameter
                    {
                        Name = edmParameter.Name,
                        In = ParameterLocation.Query, // as query option
                        Required = true,
                        Schema = new OpenApiSchema
                        {
                            Type = "string",

                            // These Parameter Objects optionally can contain the field description,
                            // whose value is the value of the unqualified annotation Core.Description of the parameter.
                            Description = context.Model.GetDescriptionAnnotation(edmParameter)
                        },

                        // The parameter description describes the format this URL-encoded JSON object or array, and/or reference to [OData-URL].
                        Description = "The URL-encoded JSON " + (edmParameter.Type.IsStructured() ? "array" : "object")
                    });
                }
                else
                {
                    // Primitive parameters use the same type mapping as described for primitive properties.
                    parameters.Add(new OpenApiParameter
                    {
                        Name = edmParameter.Name,
                        In = ParameterLocation.Path,
                        Required = true,
                        Schema = context.CreateEdmTypeSchema(edmParameter.Type)
                    });
                }
            }

            return parameters;
        }

        /// <summary>
        /// Create key parameters for the <see cref="ODataKeySegment"/>.
        /// </summary>
        /// <param name="keySegment">The key segment.</param>
        /// <returns>The created the list of <see cref="OpenApiParameter"/>.</returns>
        public static IList<OpenApiParameter> CreateKeyParameters(this ODataContext context, ODataKeySegment keySegment)
        {
            Utils.CheckArgumentNull(context, nameof(context));
            Utils.CheckArgumentNull(keySegment, nameof(keySegment));

            IList<OpenApiParameter> parameters = new List<OpenApiParameter>();
            IEdmEntityType entityType = keySegment.EntityType;

            IList<IEdmStructuralProperty> keys = entityType.Key().ToList();
            if (keys.Count() == 1)
            {
                string keyName = keys.First().Name + keySegment.KeyIndex.ToString();
                if (context.Settings.PrefixEntityTypeNameBeforeKey)
                {
                    keyName = entityType.Name + "-" + keyName;
                }

                OpenApiParameter parameter = new OpenApiParameter
                {
                    Name = keyName,
                    In = ParameterLocation.Path,
                    Required = true,
                    Description = "key: " + keyName,
                    Schema = context.CreateEdmTypeSchema(keys.First().Type)
                };

                parameter.Extensions.Add(Constants.xMsKeyType, new OpenApiString(entityType.Name));
                parameters.Add(parameter);
            }
            else
            {
                // append key parameter
                foreach (var keyProperty in entityType.Key())
                {
                    string keyName = keyProperty.Name + keySegment.KeyIndex.ToString();

                    OpenApiParameter parameter = new OpenApiParameter
                    {
                        Name = keyName,
                        In = ParameterLocation.Path,
                        Required = true,
                        Description = "key: " + keyName,
                        Schema = context.CreateEdmTypeSchema(keyProperty.Type)
                    };

                    parameter.Extensions.Add(Constants.xMsKeyType, new OpenApiString(entityType.Name));
                    parameters.Add(parameter);
                }
            }

            return parameters;
        }

        /// <summary>
        /// Create the $top parameter.
        /// </summary>
        /// <param name="context">The OData context.</param>
        /// <param name="target">The Edm annotation target.</param>
        /// <returns>The created <see cref="OpenApiParameter"/> or null.</returns>
        public static OpenApiParameter CreateTop(this ODataContext context, IEdmVocabularyAnnotatable target)
        {
            Utils.CheckArgumentNull(context, nameof(context));
            Utils.CheckArgumentNull(target, nameof(target));

            TopSupported top = context.Model.GetTopSupported(target);
            if (top == null || top.IsSupported)
            {
                return new OpenApiParameter
                {
                    Reference = new OpenApiReference { Type = ReferenceType.Parameter, Id = "top" }
                };
            }

            return null;
        }

        /// <summary>
        /// Create the $skip parameter.
        /// </summary>
        /// <param name="context">The OData context.</param>
        /// <param name="target">The Edm annotation target.</param>
        /// <returns>The created <see cref="OpenApiParameter"/> or null.</returns>
        public static OpenApiParameter CreateSkip(this ODataContext context, IEdmVocabularyAnnotatable target)
        {
            Utils.CheckArgumentNull(context, nameof(context));
            Utils.CheckArgumentNull(target, nameof(target));

            SkipSupported skip = context.Model.GetSkipSupported(target);
            if (skip == null || skip.IsSupported)
            {
                return new OpenApiParameter
                {
                    Reference = new OpenApiReference { Type = ReferenceType.Parameter, Id = "skip" }
                };
            }

            return null;
        }

        /// <summary>
        /// Create the $search parameter.
        /// </summary>
        /// <param name="context">The OData context.</param>
        /// <param name="target">The Edm annotation target.</param>
        /// <returns>The created <see cref="OpenApiParameter"/> or null.</returns>
        public static OpenApiParameter CreateSearch(this ODataContext context, IEdmVocabularyAnnotatable target)
        {
            Utils.CheckArgumentNull(context, nameof(context));
            Utils.CheckArgumentNull(target, nameof(target));

            SearchRestrictions search = context.Model.GetSearchRestrictions(target);
            if (search == null || search.IsSearchable)
            {
                return new OpenApiParameter
                {
                    Reference = new OpenApiReference { Type = ReferenceType.Parameter, Id = "search" }
                };
            }

            return null;
        }

        /// <summary>
        /// Create the $count parameter.
        /// </summary>
        /// <param name="context">The OData context.</param>
        /// <param name="target">The Edm annotation target.</param>
        /// <returns>The created <see cref="OpenApiParameter"/> or null.</returns>
        public static OpenApiParameter CreateCount(this ODataContext context, IEdmVocabularyAnnotatable target)
        {
            Utils.CheckArgumentNull(context, nameof(context));
            Utils.CheckArgumentNull(target, nameof(target));

            CountRestrictions count = context.Model.GetCountRestrictions(target);
            if (count == null || count.IsCountable)
            {
                return new OpenApiParameter
                {
                    Reference = new OpenApiReference { Type = ReferenceType.Parameter, Id = "count" }
                };
            }

            return null;
        }

        /// <summary>
        /// Create the $filter parameter.
        /// </summary>
        /// <param name="context">The OData context.</param>
        /// <param name="target">The Edm annotation target.</param>
        /// <returns>The created <see cref="OpenApiParameter"/> or null.</returns>
        public static OpenApiParameter CreateFilter(this ODataContext context, IEdmVocabularyAnnotatable target)
        {
            Utils.CheckArgumentNull(context, nameof(context));
            Utils.CheckArgumentNull(target, nameof(target));

            FilterRestrictions filter = context.Model.GetFilterRestrictions(target);
            if (filter == null || filter.IsFilterable)
            {
                return new OpenApiParameter
                {
                    Reference = new OpenApiReference { Type = ReferenceType.Parameter, Id = "filter" }
                };
            }

            return null;
        }

        public static OpenApiParameter CreateOrderBy(this ODataContext context, IEdmEntitySet entitySet)
        {
            Utils.CheckArgumentNull(context, nameof(context));
            Utils.CheckArgumentNull(entitySet, nameof(entitySet));

            return context.CreateOrderBy(entitySet, entitySet.EntityType());
        }

        public static OpenApiParameter CreateOrderBy(this ODataContext context, IEdmSingleton singleton)
        {
            Utils.CheckArgumentNull(context, nameof(context));
            Utils.CheckArgumentNull(singleton, nameof(singleton));

            return context.CreateOrderBy(singleton, singleton.EntityType());
        }

        public static OpenApiParameter CreateOrderBy(this ODataContext context, IEdmNavigationProperty navigationProperty)
        {
            Utils.CheckArgumentNull(context, nameof(context));
            Utils.CheckArgumentNull(navigationProperty, nameof(navigationProperty));

            return context.CreateOrderBy(navigationProperty, navigationProperty.ToEntityType());
        }

        /// <summary>
        /// Create $orderby parameter for the <see cref="IEdmEntitySet"/>.
        /// </summary>
        /// <param name="context">The OData context.</param>
        /// <param name="target">The Edm annotation target.</param>
        /// <returns>The created <see cref="OpenApiParameter"/> or null.</returns>
        public static OpenApiParameter CreateOrderBy(this ODataContext context, IEdmVocabularyAnnotatable target, IEdmEntityType entityType)
        {
            Utils.CheckArgumentNull(context, nameof(context));
            Utils.CheckArgumentNull(target, nameof(target));
            Utils.CheckArgumentNull(entityType, nameof(entityType));

            SortRestrictions sort = context.Model.GetSortRestrictions(target);
            if (sort != null && !sort.IsSortable)
            {
                return null;
            }

            IList<IOpenApiAny> orderByItems = new List<IOpenApiAny>();
            foreach (var property in entityType.StructuralProperties())
            {
                if (sort != null && sort.IsNonSortableProperty(property.Name))
                {
                    continue;
                }

                bool isAscOnly = sort != null ? sort.IsAscendingOnlyProperty(property.Name) : false ;
                bool isDescOnly = sort != null ? sort.IsDescendingOnlyProperty(property.Name) : false;
                if (isAscOnly || isDescOnly)
                {
                    if (isAscOnly)
                    {
                        orderByItems.Add(new OpenApiString(property.Name));
                    }
                    else
                    {
                        orderByItems.Add(new OpenApiString(property.Name + " desc"));
                    }
                }
                else
                {
                    orderByItems.Add(new OpenApiString(property.Name));
                    orderByItems.Add(new OpenApiString(property.Name + " desc"));
                }
            }

            return new OpenApiParameter
            {
                Name = "$orderby",
                In = ParameterLocation.Query,
                Description = "Order items by property values",
                Schema = new OpenApiSchema
                {
                    // Remove array in favor of single instance for PowerApps
                    Type = "string",
                    Enum = orderByItems
                },
                Style = ParameterStyle.Simple
            };
        }

        public static OpenApiParameter CreateSelect(this ODataContext context, IEdmEntitySet entitySet)
        {
            Utils.CheckArgumentNull(context, nameof(context));
            Utils.CheckArgumentNull(entitySet, nameof(entitySet));

            return context.CreateSelect(entitySet, entitySet.EntityType());
        }

        public static OpenApiParameter CreateSelect(this ODataContext context, IEdmSingleton singleton)
        {
            Utils.CheckArgumentNull(context, nameof(context));
            Utils.CheckArgumentNull(singleton, nameof(singleton));

            return context.CreateSelect(singleton, singleton.EntityType());
        }

        public static OpenApiParameter CreateSelect(this ODataContext context, IEdmNavigationProperty navigationProperty)
        {
            Utils.CheckArgumentNull(context, nameof(context));
            Utils.CheckArgumentNull(navigationProperty, nameof(navigationProperty));

            return context.CreateSelect(navigationProperty, navigationProperty.ToEntityType());
        }

        // <summary>
        /// Create $select parameter for the <see cref="IEdmNavigationSource"/>.
        /// </summary>
        /// <param name="context">The OData context.</param>
        /// <param name="navigationSource">The Edm navigation source.</param>
        /// <returns>The created <see cref="OpenApiParameter"/> or null.</returns>
        public static OpenApiParameter CreateSelect(this ODataContext context, IEdmVocabularyAnnotatable target, IEdmEntityType entityType)
        {
            Utils.CheckArgumentNull(context, nameof(context));
            Utils.CheckArgumentNull(target, nameof(target));
            Utils.CheckArgumentNull(entityType, nameof(entityType));

            NavigationRestrictions navigation = context.Model.GetNavigationRestrictions(target);
            if (navigation != null && !navigation.IsNavigable)
            {
                return null;
            }

            IList<IOpenApiAny> selectItems = new List<IOpenApiAny>();

            foreach (var property in entityType.StructuralProperties())
            {
                selectItems.Add(new OpenApiString(property.Name));
            }

            foreach (var property in entityType.NavigationProperties())
            {
                if (navigation != null && navigation.IsRestrictedProperty(property.Name))
                {
                    continue;
                }

                selectItems.Add(new OpenApiString(property.Name));
            }

            return new OpenApiParameter
            {
                Name = "$select",
                In = ParameterLocation.Query,
                Description = "Select properties to be returned",
                Schema = new OpenApiSchema
                {
                    // Remove array in favor of single instance for PowerApps
                    Type = "string",
                    Enum = selectItems
                },
                Style = ParameterStyle.Simple
            };
        }

        public static OpenApiParameter CreateExpand(this ODataContext context, IEdmEntitySet entitySet)
        {
            Utils.CheckArgumentNull(context, nameof(context));
            Utils.CheckArgumentNull(entitySet, nameof(entitySet));

            return context.CreateExpand(entitySet, entitySet.EntityType());
        }

        public static OpenApiParameter CreateExpand(this ODataContext context, IEdmSingleton singleton)
        {
            Utils.CheckArgumentNull(context, nameof(context));
            Utils.CheckArgumentNull(singleton, nameof(singleton));

            return context.CreateExpand(singleton, singleton.EntityType());
        }

        public static OpenApiParameter CreateExpand(this ODataContext context, IEdmNavigationProperty navigationProperty)
        {
            Utils.CheckArgumentNull(context, nameof(context));
            Utils.CheckArgumentNull(navigationProperty, nameof(navigationProperty));

            return context.CreateExpand(navigationProperty, navigationProperty.ToEntityType());
        }

        /// <summary>
        /// Create $expand parameter for the <see cref="IEdmNavigationSource"/>.
        /// </summary>
        /// <param name="context">The OData context.</param>
        /// <param name="target">The edm entity path.</param>
        /// <param name="entityType">The edm entity path.</param>
        /// <returns>The created <see cref="OpenApiParameter"/> or null.</returns>
        public static OpenApiParameter CreateExpand(this ODataContext context, IEdmVocabularyAnnotatable target, IEdmEntityType entityType)
        {
            Utils.CheckArgumentNull(context, nameof(context));
            Utils.CheckArgumentNull(target, nameof(target));
            Utils.CheckArgumentNull(entityType, nameof(entityType));

            ExpandRestrictions expand = context.Model.GetExpandRestrictions(target);
            if (expand != null && !expand.IsExpandable)
            {
                return null;
            }

            IList<IOpenApiAny> expandItems = new List<IOpenApiAny>
            {
                new OpenApiString("*")
            };

            foreach (var property in entityType.NavigationProperties())
            {
                if (expand != null && expand.IsNonExpandableProperty(property.Name))
                {
                    continue;
                }

                expandItems.Add(new OpenApiString(property.Name));
            }

            return new OpenApiParameter
            {
                Name = "$expand",
                In = ParameterLocation.Query,
                Description = "Expand related entities",
                Schema = new OpenApiSchema
                {
                    // Remove array in favor of single instance for PowerApps
                    Type = "string",
                    Enum = expandItems
                },
                Style = ParameterStyle.Simple
            };
        }

        // #top
        private static OpenApiParameter CreateTop(int topExample)
        {
            return new OpenApiParameter
            {
                Name = "$top",
                In = ParameterLocation.Query,
                Description = "Show only the first n items",
                Schema = new OpenApiSchema
                {
                    Type = "integer",
                    Minimum = 0,
                },
                Example = new OpenApiInteger(topExample)
            };
        }

        // $skip
        private static OpenApiParameter CreateSkip()
        {
            return new OpenApiParameter
            {
                Name = "$skip",
                In = ParameterLocation.Query,
                Description = "Skip the first n items",
                Schema = new OpenApiSchema
                {
                    Type = "integer",
                    Minimum = 0,
                }
            };
        }

        // $count
        private static OpenApiParameter CreateCount()
        {
            return new OpenApiParameter
            {
                Name = "$count",
                In = ParameterLocation.Query,
                Description = "Include count of items",
                Schema = new OpenApiSchema
                {
                    Type = "boolean"
                }
            };
        }

        // $filter
        private static OpenApiParameter CreateFilter()
        {
            return new OpenApiParameter
            {
                Name = "$filter",
                In = ParameterLocation.Query,
                Description = "Filter items by property values",
                Schema = new OpenApiSchema
                {
                    Type = "string"
                }
            };
        }

        // $search
        private static OpenApiParameter CreateSearch()
        {
            return new OpenApiParameter
            {
                Name = "$search",
                In = ParameterLocation.Query,
                Description = "Search items by search phrases",
                Schema = new OpenApiSchema
                {
                    Type = "string"
                }
            };
        }
    }
}
