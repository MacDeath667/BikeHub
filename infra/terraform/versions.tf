terraform {
  required_version = ">= 1.11.0"

  required_providers {
    truenas = {
      source  = "truenas/truenas"
      version = ">= 1.0.0, < 2.0.0"
    }
  }
}
