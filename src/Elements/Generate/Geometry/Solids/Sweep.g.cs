//----------------------
// <auto-generated>
//     Generated using the NJsonSchema v10.1.4.0 (Newtonsoft.Json v12.0.0.0) (http://NJsonSchema.org)
// </auto-generated>
//----------------------
using Elements;
using Elements.GeoJSON;
using Elements.Geometry;
using Elements.Geometry.Solids;
using Elements.Properties;
using Elements.Validators;
using System;
using System.Collections.Generic;
using System.Linq;
using Line = Elements.Geometry.Line;
using Polygon = Elements.Geometry.Polygon;

namespace Elements.Geometry.Solids
{
    #pragma warning disable // Disable all warnings

    /// <summary>A sweep of a profile along a curve.</summary>
    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "10.1.4.0 (Newtonsoft.Json v12.0.0.0)")]
    public partial class Sweep : SolidOperation, System.ComponentModel.INotifyPropertyChanged
    {
        private Profile _profile;
        private Curve _curve;
        private double _startSetback;
        private double _endSetback;
    
        [Newtonsoft.Json.JsonConstructor]
        public Sweep(Profile @profile, Curve @curve, double @startSetback, double @endSetback, bool @isVoid)
            : base(isVoid)
        {
            var validator = Validator.Instance.GetFirstValidatorForType<Sweep>();
            if(validator != null)
            {
                validator.PreConstruct(new object[]{ @profile, @curve, @startSetback, @endSetback, @isVoid});
            }
        
            this.Profile = @profile;
            this.Curve = @curve;
            this.StartSetback = @startSetback;
            this.EndSetback = @endSetback;
        
            if(validator != null)
            {
                validator.PostConstruct(this);
            }
        }
    
        /// <summary>The id of the profile to be swept along the curve.</summary>
        [Newtonsoft.Json.JsonProperty("Profile", Required = Newtonsoft.Json.Required.AllowNull)]
        public Profile Profile
        {
            get { return _profile; }
            set 
            {
                if (_profile != value)
                {
                    _profile = value; 
                    RaisePropertyChanged();
                }
            }
        }
    
        /// <summary>The curve along which the profile will be swept.</summary>
        [Newtonsoft.Json.JsonProperty("Curve", Required = Newtonsoft.Json.Required.AllowNull)]
        public Curve Curve
        {
            get { return _curve; }
            set 
            {
                if (_curve != value)
                {
                    _curve = value; 
                    RaisePropertyChanged();
                }
            }
        }
    
        /// <summary>The amount to set back the resulting solid from the start of the curve.</summary>
        [Newtonsoft.Json.JsonProperty("StartSetback", Required = Newtonsoft.Json.Required.Always)]
        public double StartSetback
        {
            get { return _startSetback; }
            set 
            {
                if (_startSetback != value)
                {
                    _startSetback = value; 
                    RaisePropertyChanged();
                }
            }
        }
    
        /// <summary>The amount to set back the resulting solid from the end of the curve.</summary>
        [Newtonsoft.Json.JsonProperty("EndSetback", Required = Newtonsoft.Json.Required.Always)]
        public double EndSetback
        {
            get { return _endSetback; }
            set 
            {
                if (_endSetback != value)
                {
                    _endSetback = value; 
                    RaisePropertyChanged();
                }
            }
        }
    
    
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        
        protected virtual void RaisePropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null) 
                handler(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    
    }
}