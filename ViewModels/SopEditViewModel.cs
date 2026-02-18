using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

public class SopEditViewModel
{
    public int Id { get; set; }

    public string SopNumber { get; set; }

    [Required, StringLength(200)]
    public string Title { get; set; }

    [Required]
    public string Revision { get; set; }

    
    public DateTime EffectiveDate { get; set; }

    [Required]
    public string ControlledBy { get; set; }

    [Required]
    public string DocType { get; set; }

    [Required(ErrorMessage = "Select at least one area")]
    public List<string> SelectedAreas { get; set; } = new();

    [Required, ValidateSteps]
    public List<StepViewModel> Steps { get; set; } = new();
    public List<int> RemovedStepIds { get; set; } = new List<int>();

    // Dropdowns for form population
    public IEnumerable<SelectListItem> AvailableAreas { get; set; }
    public IEnumerable<SelectListItem> AvailableDepartments { get; set; }
    public IEnumerable<SelectListItem> AvailableDocTypes { get; set; }
}


public class StepViewModel
{
    public int Id { get; set; }
    public int StepNumber { get; set; }
    public string Instructions { get; set; }
    public string KeyPoints { get; set; }


    // Instruction Images properties
    public List<IFormFile> StepImages { get; set; }
    public string StepImagesPaste { get; set; }
    public string DeletedImages { get; set; }
    public string ExistingImagePath { get; set; }


    // Key Point Images properties
    public IFormFile KeyPointImage { get; set; }
    public string KeyPointImagePaste { get; set; }
    public string DeletedKeyPointImage { get; set; }
    public string ExistingKeyPointImagePath { get; set; }

}


// Custom validation attribute for future date
public class FutureDateAttribute : ValidationAttribute
{
    public override bool IsValid(object value)
        => value is DateTime date && date > DateTime.Today;
}

public class ValidateStepsAttribute : ValidationAttribute
{
    protected override ValidationResult IsValid(object value, ValidationContext context)
    {
        if (value is not List<StepViewModel> steps || steps.Count == 0)
            return new ValidationResult("At least one step is required");

        foreach (var step in steps)
        {
            if (string.IsNullOrWhiteSpace(step.Instructions))
                return new ValidationResult("Each step must include instructions");
        }

        return ValidationResult.Success;
    }
}
