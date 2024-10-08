﻿using System.ComponentModel.DataAnnotations;

namespace Shared.DTO.Contact
{
    public class CreateContactDto
    {
        [Required]
        public string Name { get; set; }
        [Required]
        [EmailAddress]
        public string Email { get; set; }

        [Required]
        public string Message { get; set; }
    }
}
