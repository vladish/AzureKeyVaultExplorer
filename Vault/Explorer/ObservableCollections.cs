﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Drawing.Design;
using System.Globalization;
using System.Linq;

namespace Microsoft.PS.Common.Vault.Explorer
{
    #region ObservableCustomCollection, ExpandableCollectionObjectConverter and ExpandableCollectionEditor
    /// <summary>
    /// Simple wrapper on top of ObservableCollection, so we can enforce some validation logic plus register for:
    /// protected event PropertyChangedEventHandler PropertyChanged;
    /// </summary>
    /// <typeparam name="T">type of the item in the collection</typeparam>
    [TypeConverter(typeof(ExpandableCollectionObjectConverter))]
    public abstract class ObservableCustomCollection<T> : ObservableCollection<T>, ICustomTypeDescriptor where T : class
    {
        protected abstract PropertyDescriptor GetPropertyDescriptor(T item);

        public ObservableCustomCollection() : base() { }

        public ObservableCustomCollection(IEnumerable<T> collection) : base(collection) { }

        public void SetPropertyChangedEventHandler(PropertyChangedEventHandler propertyChanged)
        {
            PropertyChanged += propertyChanged;
        }

        public void AddOrReplace(T item)
        {
            int i = IndexOf(item);
            if (i == -1) Add(item); else SetItem(i, item);
        }

        #region ICustomTypeDescriptor interface to show properties in PropertyGrid

        public string GetComponentName() => TypeDescriptor.GetComponentName(this, true);

        public EventDescriptor GetDefaultEvent() => TypeDescriptor.GetDefaultEvent(this, true);

        public string GetClassName() => TypeDescriptor.GetClassName(this, true);

        public EventDescriptorCollection GetEvents(Attribute[] attributes) => TypeDescriptor.GetEvents(this, attributes, true);

        public EventDescriptorCollection GetEvents() => TypeDescriptor.GetEvents(this, true);

        public TypeConverter GetConverter() => TypeDescriptor.GetConverter(this, true);

        public object GetPropertyOwner(PropertyDescriptor pd) => this;

        public AttributeCollection GetAttributes() => TypeDescriptor.GetAttributes(this, true);

        public object GetEditor(Type editorBaseType) => TypeDescriptor.GetEditor(this, editorBaseType, true);

        public PropertyDescriptor GetDefaultProperty() => null;

        public PropertyDescriptorCollection GetProperties() => GetProperties(new Attribute[0]);

        public PropertyDescriptorCollection GetProperties(Attribute[] attributes)
        {
            return new PropertyDescriptorCollection((from item in this select GetPropertyDescriptor(item)).ToArray());
        }

        #endregion
    }

    /// <summary>
    /// Shows number of items in the collection in the PropertyGrid item
    /// </summary>
    public class ExpandableCollectionObjectConverter : ExpandableObjectConverter
    {
        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType) =>
            (destinationType == typeof(string) && value is ICollection) ? $"{(value as ICollection).Count} item(s)" : base.ConvertTo(context, culture, value, destinationType);
    }

    /// <summary>
    /// Our collection editor, that will force refresh the expandable properties in case collection was changed
    /// </summary>
    /// <typeparam name="T">type of the collection</typeparam>
    /// <typeparam name="U">type of the item in the collection</typeparam>
    public class ExpandableCollectionEditor<T, U> : CollectionEditor where T : ObservableCustomCollection<U> where U : class
    {
        public ExpandableCollectionEditor(Type type) : base(type) { }

        public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
        {
            T oc = value as T;
            bool changed = false;
            oc.SetPropertyChangedEventHandler((s, e) => { changed = true; });
            var collection = base.EditValue(context, provider, value);
            // If something was changed in the collection we always return a new value (copy ctor), to force refresh the expandable properties
            return (changed) ? (T)Activator.CreateInstance(typeof(T), (IEnumerable<U>)collection) : collection;
        }
    }

    #endregion

    #region TagItems

    [Editor(typeof(ExpandableCollectionEditor<ObservableTagItemsCollection, TagItem>), typeof(UITypeEditor))]
    public class ObservableTagItemsCollection : ObservableCustomCollection<TagItem>
    {
        public ObservableTagItemsCollection() : base() { }

        public ObservableTagItemsCollection(IEnumerable<TagItem> collection) : base(collection) { }

        protected override PropertyDescriptor GetPropertyDescriptor(TagItem item) => new ReadOnlyPropertyDescriptor(item.Name, item.Value);

        protected override void InsertItem(int index, TagItem item)
        {
            if (this.Count >= Consts.MaxNumberOfTags)
            {
                throw new ArgumentOutOfRangeException("Tags.Count", $"Too many tags, maximum number of tags for secret is only {Consts.MaxNumberOfTags}");
            }
            base.InsertItem(index, item);
        }
    }

    public class TagItem
    {
        private string _name;
        private string _value;

        [Category("Tag")]
        public string Name
        {
            get
            {
                return _name;
            }
            set
            {
                Guard.ArgumentNotNullOrEmptyString(value, nameof(value));
                if (value.Length > Consts.MaxTagNameLength)
                {
                    throw new ArgumentOutOfRangeException("Name.Length", $"Tag name '{value}' is too long, name can be up to {Consts.MaxTagNameLength} chars");
                }
                _name = value;
            }
        }

        [Category("Tag")]
        public string Value
        {
            get
            {
                return _value;
            }
            set
            {
                Guard.ArgumentNotNull(value, nameof(value));
                if (value.Length > Consts.MaxTagValueLength)
                {
                    throw new ArgumentOutOfRangeException("Value.Length", $"Tag value '{value}' is too long, value can be up to {Consts.MaxTagValueLength} chars");
                }
                _value = value;
            }
        }

        public TagItem() : this("name", "") { }

        public TagItem(KeyValuePair<string, string> kvp) : this(kvp.Key, kvp.Value) { }

        public TagItem(string name, string value)
        {
            Name = name;
            Value = value;
        }

        public override string ToString() => $"{Name}";

        public override bool Equals(object obj) => Equals(obj as TagItem);

        public bool Equals(TagItem ti) => (ti != null) && (0 == string.Compare(ti.Name, Name, true));

        public override int GetHashCode() => Name.GetHashCode();
    }

    #endregion

    #region LifetimeActionItems

    [Editor(typeof(ExpandableCollectionEditor<ObservableLifetimeActionsCollection, LifetimeActionItem>), typeof(UITypeEditor))]
    public class ObservableLifetimeActionsCollection : ObservableCustomCollection<LifetimeActionItem>
    {
        public ObservableLifetimeActionsCollection() : base() { }

        public ObservableLifetimeActionsCollection(IEnumerable<LifetimeActionItem> collection) : base(collection) { }

        protected override PropertyDescriptor GetPropertyDescriptor(LifetimeActionItem item) =>
            new ReadOnlyPropertyDescriptor(item.Type, $"DaysBeforeExpiry={Utils.NullableIntToString(item.DaysBeforeExpiry)}, LifetimePercentage={Utils.NullableIntToString(item.LifetimePercentage)}");
    }

    [DefaultProperty("Type")]
    [Description("Action and its trigger that will be performed by Key Vault over the lifetime of a certificate.")]
    public class LifetimeActionItem
    {
        [Category("Action")]
        public string Type { get; set; }

        [Category("Trigger")]
        public int? DaysBeforeExpiry { get; set; }

        [Category("Trigger")]
        public int? LifetimePercentage { get; set; }

        public override string ToString() => Type;
    }

    #endregion
}
